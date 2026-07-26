// MythCapture — capture this app's own window via ScreenCaptureKit + AVFoundation.
// H.264/HEVC recordings use .mp4; Apple ProRes recordings use .mov.
// Exposed over a flat C ABI (see the @_cdecl functions at the bottom) so the C# game can
// P/Invoke it. Requires Screen Recording permission (System Settings › Privacy & Security ›
// Screen Recording) for whichever process hosts the game.
//
// Output is re-timed to a constant frame rate: ScreenCaptureKit delivers frames with jittery
// capture timestamps, so we snap each frame to the nearest 1/fps slot and drop duplicates.
// Passing the raw timestamps through produces a variable-frame-rate file that judders on
// playback even when every frame is sharp.

import Foundation
import AppKit
import AVFoundation
import CoreMedia
import CoreVideo
import ScreenCaptureKit

@available(macOS 12.3, *)
private enum MythCaptureCodec: Int32 {
	// Keep 0 and 1 compatible with the previous `hevc` boolean parameter:
	// false/0 = H.264, true/1 = HEVC.
	case h264 = 0
	case hevc = 1
	case proRes422LT = 2
	case proRes422 = 3
	case proRes422HQ = 4

	var avCodec: AVVideoCodecType {
		switch self {
		case .h264: return .h264
		case .hevc: return .hevc
		case .proRes422LT: return .proRes422LT
		case .proRes422: return .proRes422
		case .proRes422HQ: return .proRes422HQ
		}
	}

	var fileType: AVFileType {
		switch self {
		case .h264, .hevc: return .mp4
		case .proRes422LT, .proRes422, .proRes422HQ: return .mov
		}
	}

	var requiredPathExtension: String {
		fileType == .mov ? "mov" : "mp4"
	}

	var displayName: String {
		switch self {
		case .h264: return "H.264"
		case .hevc: return "HEVC"
		case .proRes422LT: return "Apple ProRes 422 LT"
		case .proRes422: return "Apple ProRes 422"
		case .proRes422HQ: return "Apple ProRes 422 HQ"
		}
	}

	var defaultBitsPerPixelPerFrame: Double? {
		switch self {
		case .h264: return 0.20
		case .hevc: return 0.12
		case .proRes422LT, .proRes422, .proRes422HQ: return nil
		}
	}
}

@available(macOS 12.3, *)
final class MythRecorder: NSObject, SCStreamOutput {
	static let shared = MythRecorder()

	private let lock = NSLock()
	private let sampleQueue = DispatchQueue(label: "com.myth.capture.samples")

	private var stream: SCStream?
	private var writer: AVAssetWriter?
	private var input: AVAssetWriterInput?
	private var adaptor: AVAssetWriterInputPixelBufferAdaptor?
	private var recordingFlag = false
	private var lastErr = ""

	// constant-frame-rate re-timing state
	private var fps: Int32 = 60
	private var outputWidth: Int32 = 0
	private var outputHeight: Int32 = 0
	private var startPTS: CMTime = .invalid
	private var lastIndex: Int64 = -1

	var isRecording: Bool { lock.lock(); defer { lock.unlock() }; return recordingFlag }
	var lastError: String { lock.lock(); defer { lock.unlock() }; return lastErr }
	var effectiveFps: Int32 { lock.lock(); defer { lock.unlock() }; return fps }
	var width: Int32 { lock.lock(); defer { lock.unlock() }; return outputWidth }
	var height: Int32 { lock.lock(); defer { lock.unlock() }; return outputHeight }
	private func setError(_ s: String) { lock.lock(); lastErr = s; lock.unlock() }

	// The display refresh rate (capped) — the game is vsync'd to it, so capturing at this
	// rate gives a 1:1 source→output frame mapping and the smoothest motion.
	private func autoFps() -> Int32 {
		let rate = (NSScreen.main ?? NSScreen.screens.first)?.maximumFramesPerSecond ?? 0
		return rate > 0 ? min(120, Int32(rate)) : 60
	}

	func start(path: String, fps requestedFps: Int32, bitrateBps: Int, maxHeight: Int, codec rawCodec: Int32) -> Bool {
		if isRecording { setError("already recording"); return false }
		setError("")
		guard let codec = MythCaptureCodec(rawValue: rawCodec) else {
			setError("unsupported codec \(rawCodec); expected 0...4")
			return false
		}
		let fps = requestedFps > 0 ? requestedFps : autoFps()

		// 1. Find our own on-screen window (largest, in case of helper windows).
		let contentSem = DispatchSemaphore(value: 0)
		var window: SCWindow?
		SCShareableContent.getExcludingDesktopWindows(false, onScreenWindowsOnly: true) { content, error in
			if let error {
				self.setError("shareable content: \(error.localizedDescription)")
			} else if let content {
				let pid = ProcessInfo.processInfo.processIdentifier
				window = content.windows
					.filter { $0.owningApplication?.processID == pid && $0.isOnScreen && $0.frame.width > 2 && $0.frame.height > 2 }
					.max { $0.frame.width * $0.frame.height < $1.frame.width * $1.frame.height }
				if window == nil { self.setError("no on-screen window for this app (pid \(pid)); grant Screen Recording permission") }
			}
			contentSem.signal()
		}
		if contentSem.wait(timeout: .now() + 5) == .timedOut {
			setError("timed out querying windows (Screen Recording permission?)")
			return false
		}
		guard let window else { return false }

		// 2. Stream configuration sized to the window's real pixel dimensions (optionally capped).
		//    Strip the chrome: drop the title bar via a source crop and ignore the shadow.
		let filter = SCContentFilter(desktopIndependentWindow: window)
		let nsWindow = NSApp.windows.first { $0.windowNumber == Int(window.windowID) }
		let scale: CGFloat = {
			if #available(macOS 14.0, *) { return CGFloat(filter.pointPixelScale) }
			return nsWindow?.backingScaleFactor ?? 2
		}()
		let titleBar = max(0, nsWindow.map { $0.frame.height - $0.contentLayoutRect.height } ?? 0)
		let contentW = window.frame.width
		let contentH = window.frame.height - titleBar

		var w = Int((contentW * scale).rounded())
		var h = Int((contentH * scale).rounded())
		if maxHeight > 0 && h > maxHeight {
			w = Int((Double(w) * Double(maxHeight) / Double(h)).rounded())
			h = maxHeight
		}
		w -= w % 2; h -= h % 2   // 4:2:0 and 4:2:2 codecs require even dimensions
		if w < 2 || h < 2 { setError("degenerate window size"); return false }

		let config = SCStreamConfiguration()
		config.width = w
		config.height = h
		config.sourceRect = CGRect(x: 0, y: titleBar, width: contentW, height: contentH)
		config.pixelFormat = kCVPixelFormatType_32BGRA
		config.minimumFrameInterval = CMTime(value: 1, timescale: fps)
		config.queueDepth = 8
		config.showsCursor = false
		if #available(macOS 13.0, *) { config.capturesAudio = false }
		if #available(macOS 14.0, *) {
			config.ignoreShadowsSingleWindow = true
			config.ignoreGlobalClipSingleWindow = true
		}

		// 3. Asset writer. H.264/HEVC use explicit bitrate controls; ProRes chooses
		//    its own profile-defined bitrate and should not receive H.264-style settings.
		let url = URL(fileURLWithPath: path)
		guard url.pathExtension.lowercased() == codec.requiredPathExtension else {
			setError("\(codec.displayName) requires a .\(codec.requiredPathExtension) output path")
			return false
		}

		try? FileManager.default.removeItem(at: url)
		guard let writer = try? AVAssetWriter(outputURL: url, fileType: codec.fileType) else {
			setError("could not create \(codec.displayName) writer at \(path)")
			return false
		}

		var settings: [String: Any] = [
			AVVideoCodecKey: codec.avCodec,
			AVVideoWidthKey: w,
			AVVideoHeightKey: h,
		]

		if let defaultRate = codec.defaultBitsPerPixelPerFrame {
			let bitrate = bitrateBps > 0
				? bitrateBps
				: Int(Double(w * h * Int(fps)) * defaultRate)

			var compression: [String: Any] = [
				AVVideoAverageBitRateKey: bitrate,
				AVVideoExpectedSourceFrameRateKey: Int(fps),
				AVVideoMaxKeyFrameIntervalKey: Int(fps) * 2,
				AVVideoAllowFrameReorderingKey: false,   // no B-frames: crisp timing, low latency
			]
			if codec == .h264 {
				compression[AVVideoProfileLevelKey] = AVVideoProfileLevelH264HighAutoLevel
			}
			settings[AVVideoCompressionPropertiesKey] = compression
		}

		guard writer.canApply(outputSettings: settings, forMediaType: .video) else {
			setError("\(codec.displayName) is not supported for \(w)x\(h) at \(fps) fps on this Mac")
			return false
		}

		let input = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
		input.expectsMediaDataInRealTime = true
		let adaptor = AVAssetWriterInputPixelBufferAdaptor(
			assetWriterInput: input,
			sourcePixelBufferAttributes: [
				kCVPixelBufferPixelFormatTypeKey as String: Int(kCVPixelFormatType_32BGRA),
				kCVPixelBufferWidthKey as String: w,
				kCVPixelBufferHeightKey as String: h,
			])
		guard writer.canAdd(input) else { setError("cannot add writer input"); return false }
		writer.add(input)

		// 4. Wire up the stream.
		let stream = SCStream(filter: filter, configuration: config, delegate: nil)
		do { try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: sampleQueue) }
		catch { setError("addStreamOutput: \(error.localizedDescription)"); return false }

		lock.lock()
		self.writer = writer; self.input = input; self.adaptor = adaptor; self.stream = stream
		self.fps = fps; self.outputWidth = Int32(w); self.outputHeight = Int32(h)
		self.startPTS = .invalid; self.lastIndex = -1
		lock.unlock()

		let startSem = DispatchSemaphore(value: 0)
		var startOk = false
		stream.startCapture { error in
			if let error { self.setError("startCapture: \(error.localizedDescription)") } else { startOk = true }
			startSem.signal()
		}
		if startSem.wait(timeout: .now() + 5) == .timedOut { setError("startCapture timed out"); return false }
		if !startOk { return false }

		lock.lock(); recordingFlag = true; lock.unlock()
		return true
	}

	func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
		guard type == .screen, CMSampleBufferIsValid(sampleBuffer), CMSampleBufferGetNumSamples(sampleBuffer) > 0 else { return }

		// Skip frames the compositor marks incomplete/idle.
		if let arr = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
		   let info = arr.first,
		   let raw = info[.status] as? Int,
		   let status = SCFrameStatus(rawValue: raw),
		   status != .complete {
			return
		}

		guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }

		lock.lock()
		let writer = self.writer, input = self.input, adaptor = self.adaptor
		let fps = self.fps
		var start = self.startPTS
		let last = self.lastIndex
		lock.unlock()
		guard let writer, let input, let adaptor else { return }

		let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)

		if writer.status == .unknown {
			writer.startWriting()
			writer.startSession(atSourceTime: .zero)
		}
		if writer.status == .failed { setError(writer.error?.localizedDescription ?? "writer failed"); return }

		if start == .invalid { start = pts; lock.lock(); startPTS = pts; lock.unlock() }

		// Snap to the constant-frame-rate grid; drop anything landing in an already-used slot.
		let elapsed = CMTimeGetSeconds(CMTimeSubtract(pts, start))
		let index = Int64((elapsed * Double(fps)).rounded())
		if index <= last { return }
		guard input.isReadyForMoreMediaData else { return }

		if adaptor.append(pixelBuffer, withPresentationTime: CMTime(value: index, timescale: fps)) {
			lock.lock(); lastIndex = index; lock.unlock()
		}
	}

	func stop() -> Bool {
		if !isRecording { setError("not recording"); return false }
		lock.lock(); recordingFlag = false; let stream = self.stream; lock.unlock()

		let stopSem = DispatchSemaphore(value: 0)
		stream?.stopCapture { _ in stopSem.signal() }
		_ = stopSem.wait(timeout: .now() + 5)

		lock.lock(); let input = self.input, writer = self.writer; lock.unlock()
		input?.markAsFinished()
		let finishSem = DispatchSemaphore(value: 0)
		writer?.finishWriting { finishSem.signal() }
		_ = finishSem.wait(timeout: .now() + 10)

		let ok = (writer?.status == .completed)
		if !ok { setError(writer?.error?.localizedDescription ?? "writer did not complete") }

		lock.lock()
		self.stream = nil; self.writer = nil; self.input = nil; self.adaptor = nil
		self.startPTS = .invalid; self.lastIndex = -1
		lock.unlock()
		return ok
	}
}

// MARK: - C ABI

// codec values:
//   0 = H.264          (.mp4; bitrateBps is used)
//   1 = HEVC           (.mp4; bitrateBps is used; retained for ABI compatibility)
//   2 = ProRes 422 LT  (.mov; bitrateBps is ignored)
//   3 = ProRes 422     (.mov; bitrateBps is ignored)
//   4 = ProRes 422 HQ  (.mov; bitrateBps is ignored)
@_cdecl("myth_capture_start")
public func myth_capture_start(_ path: UnsafePointer<CChar>, _ fps: Int32, _ bitrateBps: Int32, _ maxHeight: Int32, _ codec: Int32) -> Int32 {
	guard #available(macOS 12.3, *) else { return -2 }
	return MythRecorder.shared.start(
		path: String(cString: path),
		fps: fps,
		bitrateBps: Int(bitrateBps),
		maxHeight: Int(maxHeight),
		codec: codec
	) ? 0 : -1
}

@_cdecl("myth_capture_stop")
public func myth_capture_stop() -> Int32 {
	guard #available(macOS 12.3, *) else { return -2 }
	return MythRecorder.shared.stop() ? 0 : -1
}

@_cdecl("myth_capture_is_recording")
public func myth_capture_is_recording() -> Int32 {
	guard #available(macOS 12.3, *) else { return 0 }
	return MythRecorder.shared.isRecording ? 1 : 0
}

@_cdecl("myth_capture_fps")
public func myth_capture_fps() -> Int32 {
	guard #available(macOS 12.3, *) else { return 0 }
	return MythRecorder.shared.effectiveFps
}

@_cdecl("myth_capture_width")
public func myth_capture_width() -> Int32 {
	guard #available(macOS 12.3, *) else { return 0 }
	return MythRecorder.shared.width
}

@_cdecl("myth_capture_height")
public func myth_capture_height() -> Int32 {
	guard #available(macOS 12.3, *) else { return 0 }
	return MythRecorder.shared.height
}

@_cdecl("myth_capture_last_error")
public func myth_capture_last_error(_ buffer: UnsafeMutablePointer<CChar>?, _ capacity: Int32) {
	guard let buffer, capacity > 0 else { return }
	var msg = "unavailable (needs macOS 12.3+)"
	if #available(macOS 12.3, *) { msg = MythRecorder.shared.lastError }
	msg.withCString { _ = strlcpy(buffer, $0, Int(capacity)) }
}
