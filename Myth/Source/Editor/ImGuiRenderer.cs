using System.Diagnostics;
using System.Numerics;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

public class ImGuiRenderer : IDisposable
{
#if BROWSER
	const string BrowserVertexShader = """
struct UniformBlockType {
	Matrix: mat4x4<f32>,
}

struct VertexOutput {
	@location(0) TexCoord: vec2<f32>,
	@location(1) Color: vec4<f32>,
	@builtin(position) Position: vec4<f32>,
}

@group(1) @binding(0)
var<uniform> UniformBlock: UniformBlockType;

@vertex
fn vertex_main(
	@location(0) position: vec2<f32>,
	@location(1) texCoord: vec2<f32>,
	@location(2) color: vec4<f32>
) -> VertexOutput {
	var output: VertexOutput;
	output.TexCoord = texCoord;
	output.Color = color;
	output.Position = vec4<f32>(position.x, position.y, 0.0, 1.0) * transpose(UniformBlock.Matrix);
	return output;
}
""";
#endif

	private readonly App app;
	private readonly IntPtr context;
	private readonly Mesh<PosTexColVertex, ushort> mesh;
	private readonly Material material;
	private readonly Texture fontTexture;
	private readonly List<Texture> boundTextures = [];
	private readonly List<Batcher> batchersUsed = [];
	private readonly Stack<Batcher> batchersStack = [];
	private readonly Stack<Batcher> batcherPool = [];
	private readonly List<(ImGuiKey, Keys)> keys =
	[
		(ImGuiKey.Tab, Keys.Tab),
		(ImGuiKey.LeftArrow, Keys.Left),
		(ImGuiKey.RightArrow, Keys.Right),
		(ImGuiKey.UpArrow, Keys.Up),
		(ImGuiKey.DownArrow, Keys.Down),
		(ImGuiKey.PageUp, Keys.PageUp),
		(ImGuiKey.PageDown, Keys.PageDown),
		(ImGuiKey.Home, Keys.Home),
		(ImGuiKey.End, Keys.End),
		(ImGuiKey.Insert, Keys.Insert),
		(ImGuiKey.Delete, Keys.Delete),
		(ImGuiKey.Backspace, Keys.Backspace),
		(ImGuiKey.Space, Keys.Space),
		(ImGuiKey.Enter, Keys.Enter),
		(ImGuiKey.Escape, Keys.Escape),
		(ImGuiKey.LeftCtrl, Keys.LeftControl),
		(ImGuiKey.LeftShift, Keys.LeftShift),
		(ImGuiKey.LeftAlt, Keys.LeftAlt),
		(ImGuiKey.LeftSuper, Keys.LeftOS),
		(ImGuiKey.RightCtrl, Keys.RightControl),
		(ImGuiKey.RightShift, Keys.RightShift),
		(ImGuiKey.RightAlt, Keys.RightAlt),
		(ImGuiKey.RightSuper, Keys.RightOS),
		(ImGuiKey.Menu, Keys.Menu),
		(ImGuiKey._0, Keys.D0),
		(ImGuiKey._1, Keys.D1),
		(ImGuiKey._2, Keys.D2),
		(ImGuiKey._3, Keys.D3),
		(ImGuiKey._4, Keys.D4),
		(ImGuiKey._5, Keys.D5),
		(ImGuiKey._6, Keys.D6),
		(ImGuiKey._7, Keys.D7),
		(ImGuiKey._8, Keys.D8),
		(ImGuiKey._9, Keys.D9),
		(ImGuiKey.A, Keys.A),
		(ImGuiKey.B, Keys.B),
		(ImGuiKey.C, Keys.C),
		(ImGuiKey.D, Keys.D),
		(ImGuiKey.E, Keys.E),
		(ImGuiKey.F, Keys.F),
		(ImGuiKey.G, Keys.G),
		(ImGuiKey.H, Keys.H),
		(ImGuiKey.I, Keys.I),
		(ImGuiKey.J, Keys.J),
		(ImGuiKey.K, Keys.K),
		(ImGuiKey.L, Keys.L),
		(ImGuiKey.M, Keys.M),
		(ImGuiKey.N, Keys.N),
		(ImGuiKey.O, Keys.O),
		(ImGuiKey.P, Keys.P),
		(ImGuiKey.Q, Keys.Q),
		(ImGuiKey.R, Keys.R),
		(ImGuiKey.S, Keys.S),
		(ImGuiKey.T, Keys.T),
		(ImGuiKey.U, Keys.U),
		(ImGuiKey.V, Keys.V),
		(ImGuiKey.W, Keys.W),
		(ImGuiKey.X, Keys.X),
		(ImGuiKey.Y, Keys.Y),
		(ImGuiKey.Z, Keys.Z),
		(ImGuiKey.F1, Keys.F1),
		(ImGuiKey.F2, Keys.F2),
		(ImGuiKey.F3, Keys.F3),
		(ImGuiKey.F4, Keys.F4),
		(ImGuiKey.F5, Keys.F5),
		(ImGuiKey.F6, Keys.F6),
		(ImGuiKey.F7, Keys.F7),
		(ImGuiKey.F8, Keys.F8),
		(ImGuiKey.F9, Keys.F9),
		(ImGuiKey.F10, Keys.F10),
		(ImGuiKey.F11, Keys.F11),
		(ImGuiKey.F12, Keys.F12),
		(ImGuiKey.Apostrophe, Keys.Apostrophe),
		(ImGuiKey.Comma, Keys.Comma),
		(ImGuiKey.Minus, Keys.Minus),
		(ImGuiKey.Period, Keys.Period),
		(ImGuiKey.Slash, Keys.Slash),
		(ImGuiKey.Semicolon, Keys.Semicolon),
		(ImGuiKey.Equal, Keys.Equals),
		(ImGuiKey.LeftBracket, Keys.LeftBracket),
		(ImGuiKey.Backslash, Keys.Backslash),
		(ImGuiKey.RightBracket, Keys.RightBracket),
		(ImGuiKey.GraveAccent, Keys.Tilde),
		(ImGuiKey.CapsLock, Keys.Capslock),
		(ImGuiKey.ScrollLock, Keys.ScrollLock),
		(ImGuiKey.NumLock, Keys.Numlock),
		(ImGuiKey.PrintScreen, Keys.PrintScreen),
		(ImGuiKey.Pause, Keys.Pause),
		(ImGuiKey.Keypad0, Keys.Keypad0),
		(ImGuiKey.Keypad1, Keys.Keypad1),
		(ImGuiKey.Keypad2, Keys.Keypad2),
		(ImGuiKey.Keypad3, Keys.Keypad3),
		(ImGuiKey.Keypad4, Keys.Keypad4),
		(ImGuiKey.Keypad5, Keys.Keypad5),
		(ImGuiKey.Keypad6, Keys.Keypad6),
		(ImGuiKey.Keypad7, Keys.Keypad7),
		(ImGuiKey.Keypad8, Keys.Keypad8),
		(ImGuiKey.Keypad9, Keys.Keypad9),
		(ImGuiKey.KeypadDecimal, Keys.KeypadPeroid),
		(ImGuiKey.KeypadDivide, Keys.KeypadDivide),
		(ImGuiKey.KeypadMultiply, Keys.KeypadMultiply),
		(ImGuiKey.KeypadSubtract, Keys.KeypadMinus),
		(ImGuiKey.KeypadAdd, Keys.KeypadPlus),
		(ImGuiKey.KeypadEnter, Keys.KeypadEnter),
		(ImGuiKey.KeypadEqual, Keys.KeypadEquals),
	];

	private PosTexColVertex[] vertices = [];
	private ushort[] indices = [];

	private readonly bool gamepadNav;

	/// <summary>
	/// UI Scaling
	/// </summary>
	public float Scale = 2.0f;

	/// <summary>
	/// Mouse Position relative to ImGui elements
	/// </summary>
	public Vector2 MousePosition => app?.Input.Mouse.Position / Scale ?? Vector2.Zero;

	/// <summary>
	/// If the ImGui Context wants text input
	/// </summary>
	public bool WantsTextInput { get; private set; }

	/// <summary>
	/// If the ImGui Context wants keyboard input
	/// </summary>
	public bool WantsKeyboardInput { get; private set; }

	/// <summary>
	/// If the ImGui Context wants mouse input
	/// </summary>
	public bool WantsMouseInput { get; private set; }

	public ImGuiRenderer(App app, string? customFontPath = null, bool enableGamepadNav = false)
	{
		this.app = app;
		this.gamepadNav = enableGamepadNav;

		// create imgui context
		context = ImGui.CreateContext(null);
		ImGui.SetCurrentContext(context);

		var io = ImGui.GetIO();
		io.BackendFlags = ImGuiBackendFlags.None;
		io.ConfigFlags = ImGuiConfigFlags.DockingEnable;
		if (gamepadNav)
		{
			io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.NavEnableGamepad;
			io.BackendFlags |= ImGuiBackendFlags.HasGamepad;
		}

		// load ImGui Font
		{
			if (customFontPath != null && !File.Exists(customFontPath))
			{
				Console.Error.WriteLine($"Could not find custom font at {customFontPath}");
				Console.Error.WriteLine($"Current working dir is: {Directory.GetCurrentDirectory()}");
			}

			if (customFontPath != null && File.Exists(customFontPath))
			{
				io.Fonts.AddFontFromFileTTF(customFontPath, 64);
				io.FontGlobalScale = 16.0f / 64.0f;
			}
			else
			{
				io.Fonts.AddFontDefault();
			}
		}

		// create font texture
		unsafe
		{
			io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bytesPerPixel);
			fontTexture = new Texture(app.GraphicsDevice, width, height, new ReadOnlySpan<byte>(pixelData, width * height * 4));
		}

		// create drawing resources
		mesh = new Mesh<PosTexColVertex, ushort>(app.GraphicsDevice);
		material = app.GraphicsDevice.Defaults.TexturedMaterial.Clone();
#if BROWSER
		material.Vertex.Shader = new Shader(
			app.GraphicsDevice,
			ShaderStage.Vertex,
			System.Text.Encoding.UTF8.GetBytes(BrowserVertexShader),
			uniformBufferCount: 1,
			entryPoint: "vertex_main",
			name: "ImGuiVertex");
#endif
		ImGui.SetCurrentContext(nint.Zero);
	}

	~ImGuiRenderer() => Dispose();

	/// <summary>
	/// Begins a new ImGui Frame.
	/// ImGui methods are available between BeginLayout and EndLayout.
	/// </summary>
	public void BeginLayout()
	{
		Debug.Assert(ImGui.GetCurrentContext() == nint.Zero);
		ImGui.SetCurrentContext(context);
		var window = app.Window ?? throw new InvalidOperationException("ImGui layout requires a window.");

		// clear textures for the next frame
		boundTextures.Clear();

		// clear batches
		batchersStack.Clear();
		batchersUsed.ForEach(batcherPool.Push);
		batchersUsed.Clear();

		// assign font texture again
		var io = ImGui.GetIO();
		io.Fonts.SetTexID(GetTextureID(fontTexture));

		// setup io
		io.DeltaTime = app.Time.Delta;
		io.DisplaySize = new Vector2(window.WidthInPixels / Scale, window.HeightInPixels / Scale);
		io.DisplayFramebufferScale = Vector2.One * Scale;

		io.AddMousePosEvent(MousePosition.X, MousePosition.Y);
		io.AddMouseButtonEvent(0, app.Input.Mouse.LeftDown || app.Input.Mouse.LeftPressed);
		io.AddMouseButtonEvent(1, app.Input.Mouse.RightDown || app.Input.Mouse.RightPressed);
		io.AddMouseButtonEvent(2, app.Input.Mouse.MiddleDown || app.Input.Mouse.MiddlePressed);
		io.AddMouseWheelEvent(app.Input.Mouse.Wheel.X, app.Input.Mouse.Wheel.Y);

		foreach (var k in keys)
		{
			if (app.Input.Keyboard.Pressed(k.Item2))
				io.AddKeyEvent(k.Item1, true);
			if (app.Input.Keyboard.Released(k.Item2))
				io.AddKeyEvent(k.Item1, false);
		}

		io.AddKeyEvent(ImGuiKey.ModShift, app.Input.Keyboard.Shift);
		io.AddKeyEvent(ImGuiKey.ModAlt, app.Input.Keyboard.Alt);
		io.AddKeyEvent(ImGuiKey.ModCtrl, app.Input.Keyboard.Ctrl);
		io.AddKeyEvent(ImGuiKey.ModSuper, app.Input.Keyboard.Down(Keys.LeftOS) || app.Input.Keyboard.Down(Keys.RightOS));

		if (app.Input.Keyboard.Text.Length > 0)
		{
			for (int i = 0; i < app.Input.Keyboard.Text.Length; i++)
				io.AddInputCharacter(app.Input.Keyboard.Text[i]);
		}

		if (gamepadNav)
			FeedGamepadNav(io);

		ImGui.NewFrame();
	}

	// Drives ImGui's built-in gamepad navigation from any connected controller's
	// d-pad / left stick / face buttons. Only used when the context opted in via
	// enableGamepadNav; other contexts (e.g. the editor) are unaffected.
	private void FeedGamepadNav(ImGuiIOPtr io)
	{
		const float stickDeadzone = 0.5f;

		var up = false;
		var down = false;
		var left = false;
		var right = false;
		var activate = false;
		var cancel = false;

		foreach (var controller in app.Input.Controllers)
		{
			if (!controller.Connected)
				continue;

			up |= controller.Down(Buttons.Up);
			down |= controller.Down(Buttons.Down);
			left |= controller.Down(Buttons.Left);
			right |= controller.Down(Buttons.Right);
			activate |= controller.Down(Buttons.South);
			cancel |= controller.Down(Buttons.East);

			var stick = controller.LeftStick;
			up |= stick.Y < -stickDeadzone;
			down |= stick.Y > stickDeadzone;
			left |= stick.X < -stickDeadzone;
			right |= stick.X > stickDeadzone;
		}

		io.AddKeyEvent(ImGuiKey.GamepadDpadUp, up);
		io.AddKeyEvent(ImGuiKey.GamepadDpadDown, down);
		io.AddKeyEvent(ImGuiKey.GamepadDpadLeft, left);
		io.AddKeyEvent(ImGuiKey.GamepadDpadRight, right);
		io.AddKeyEvent(ImGuiKey.GamepadFaceDown, activate);
		io.AddKeyEvent(ImGuiKey.GamepadFaceRight, cancel);
	}

	/// <summary>
	/// Ends an ImGui Frame.
	/// Call this at the end of your Update method.
	/// </summary>
	public void EndLayout()
	{
		Debug.Assert(ImGui.GetCurrentContext() == context);
		var io = ImGui.GetIO();
		WantsTextInput = io.WantTextInput;
		WantsKeyboardInput = io.WantCaptureKeyboard;
		WantsMouseInput = io.WantCaptureMouse;
		ImGui.Render();
		ImGui.SetCurrentContext(nint.Zero);
	}

	/// <summary>
	/// Begin a new Batch in an ImGui Window. Returns true if any batch contents
	/// will be visible. Call <see cref="EndBatch"/> regardless of return value.
	/// </summary>
	public bool BeginBatch(out Batcher batch, out Rect bounds)
	{
		return BeginBatch(ImGui.GetContentRegionAvail(), out batch, out bounds);
	}

	/// <summary>
	/// Begin a new Batch in an ImGui Window. Returns true if any batch contents
	/// will be visible. Call <see cref="EndBatch"/> regardless of return value.
	/// </summary>
	public bool BeginBatch(Vector2 size, out Batcher batch, out Rect bounds)
	{
		var min = ImGui.GetCursorScreenPos();
		var max = min + size;

		// create a dummy element of the given size
		ImGui.Dummy(size);

		return BeginBatchAt(Rect.Between(min, max), out batch, out bounds);
	}

	public bool BeginBatchAt(Rect screenspace, out Batcher batch, out Rect bounds)
	{
		var clip = Rect.Between(ImGui.GetWindowDrawList().GetClipRectMin(), ImGui.GetWindowDrawList().GetClipRectMax());
		var scissor = screenspace.GetIntersection(clip).Scale(Scale).Int();

		// get recycled batcher, add to list
		batch = batcherPool.Count > 0 ? batcherPool.Pop() : new Batcher(app.GraphicsDevice);
		batch.Clear();
		batchersUsed.Add(batch);
		batchersStack.Push(batch);

		// notify imgui
		ImGui.GetWindowDrawList().AddCallback(new IntPtr(batchersUsed.Count), new IntPtr(0));

		// push relative coords
		batch.PushScissor(scissor);
		batch.PushMatrix(Matrix3x2.CreateScale(Scale));
		batch.PushMatrix(screenspace.TopLeft);

		bounds = new Rect(0, 0, screenspace.Width, screenspace.Height);

		return scissor.Width > 0 && scissor.Height > 0;
	}

	/// <summary>
	/// End a Batch in an ImGui Window
	/// </summary>
	public void EndBatch()
	{
		var batch = batchersStack.Pop();
		batch.PopMatrix();
		batch.PopMatrix();
		batch.PopScissor();
	}

	/// <summary>
	/// Renders the ImGui buffers. Call this in your Render method.
	/// </summary>
	public unsafe void Render()
	{
		Debug.Assert(ImGui.GetCurrentContext() == nint.Zero);
		ImGui.SetCurrentContext(context);
		var window = app.Window ?? throw new InvalidOperationException("ImGui rendering requires a window.");

		var data = ImGui.GetDrawData();
		if (data.NativePtr == null || data.TotalVtxCount <= 0)
		{
			ImGui.SetCurrentContext(nint.Zero);
			return;
		}

		// build vertex/index buffer lists
		{
			// calculate total size
			var vertexCount = 0;
			var indexCount = 0;
			for (int i = 0; i < data.CmdListsCount; i++)
			{
				vertexCount += data.CmdLists[i].VtxBuffer.Size;
				indexCount += data.CmdLists[i].IdxBuffer.Size;
			}

			// make sure we have enough space
			if (vertexCount > vertices.Length)
				Array.Resize(ref vertices, vertexCount);
			if (indexCount > indices.Length)
				Array.Resize(ref indices, indexCount);

			// copy data to arrays
			vertexCount = indexCount = 0;
			for (int i = 0; i < data.CmdListsCount; i++)
			{
				var list = data.CmdLists[i];
				var vertexSrc = new Span<ImDrawVert>((void*)list.VtxBuffer.Data, list.VtxBuffer.Size);
				var indexSrc = new Span<ushort>((void*)list.IdxBuffer.Data, list.IdxBuffer.Size);

				for (var v = 0; v < vertexSrc.Length; v++)
					vertices[vertexCount + v] = new PosTexColVertex(
						vertexSrc[v].pos,
						vertexSrc[v].uv,
						FromImGuiColor(vertexSrc[v].col));
				indexSrc.CopyTo(indices.AsSpan()[indexCount..]);

				vertexCount += vertexSrc.Length;
				indexCount += indexSrc.Length;
			}

			// upload buffers to mesh
			mesh.SetVertices(vertices.AsSpan(0, vertexCount));
			mesh.SetIndices(indices.AsSpan(0, indexCount));
		}

		var size = new Point2(window.WidthInPixels, window.HeightInPixels);

		// setup ortho matrix. Match Foster's Batcher (near 0, far float.MaxValue): imgui
		// verts sit at z=0, and a near plane > 0 clips them away on WebGPU.
		Matrix4x4 mat =
			Matrix4x4.CreateScale(data.FramebufferScale.X, data.FramebufferScale.Y, 1.0f) *
			Matrix4x4.CreateOrthographicOffCenter(0, size.X, size.Y, 0, 0, float.MaxValue);
		material.Vertex.SetUniformBuffer(mat);

		// draw imgui buffers to the screen
		var globalVtxOffset = 0;
		var globalIdxOffset = 0;
		for (int i = 0; i < data.CmdListsCount; i++)
		{
			var imList = data.CmdLists[i];
			var imCommands = (ImDrawCmd*)imList.CmdBuffer.Data;

			// draw each command
			for (ImDrawCmd* cmd = imCommands; cmd < imCommands + imList.CmdBuffer.Size; cmd++)
			{
				var scissor = new Rect(
					cmd->ClipRect.X,
					cmd->ClipRect.Y,
					cmd->ClipRect.Z - cmd->ClipRect.X,
					cmd->ClipRect.W - cmd->ClipRect.Y).Scale(data.FramebufferScale).Int();

				if (scissor.Width <= 0 || scissor.Height <= 0)
					continue;

				if (cmd->UserCallback != IntPtr.Zero)
				{
					var batchIndex = cmd->UserCallback.ToInt32() - 1;
					if (batchIndex >= 0 && batchIndex < batchersUsed.Count)
						batchersUsed[batchIndex].Render(window, viewport: null, scissor: scissor);
				}
				else
				{
					var textureIndex = cmd->TextureId.ToInt32();
					Texture? texture = null;
					if (textureIndex < boundTextures.Count)
						texture = boundTextures[textureIndex];

					material.Fragment.Samplers[0] = new(texture, new TextureSampler(TextureFilter.Nearest, TextureWrap.Clamp));

					app.GraphicsDevice.Draw(new DrawCommand(window, mesh, material)
					{
						BlendMode = new BlendMode(BlendOp.Add, BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha),
						VertexOffset = (int)(cmd->VtxOffset + globalVtxOffset),
						IndexOffset = (int)(cmd->IdxOffset + globalIdxOffset),
						IndexCount = (int)cmd->ElemCount,
						DepthTestEnabled = false,
						DepthWriteEnabled = false,
						Scissor = scissor
					});
				}
			}

			globalVtxOffset += imList.VtxBuffer.Size;
			globalIdxOffset += imList.IdxBuffer.Size;
		}

		ImGui.SetCurrentContext(nint.Zero);
	}

	private static Color FromImGuiColor(uint color)
		=> new(
			(byte)(color >> 0),
			(byte)(color >> 8),
			(byte)(color >> 16),
			(byte)(color >> 24));

	/// <summary>
	/// Gets a Texture ID to draw in ImGui
	/// </summary>
	public IntPtr GetTextureID(Texture? texture)
	{
		var id = new IntPtr(boundTextures.Count);
		if (texture != null)
			boundTextures.Add(texture);
		return id;
	}

	public bool ImageButton(ReadOnlySpan<char> id, in Subtexture subtexture, Vector2 size, bool selected = false, Vector4? selectedColor = null, bool fitAspectRatio = false, Vector2? imagePadding = null)
	{
		if (subtexture.Texture == null)
			return ImGui.Button(id, size);

		var pressed = ImGui.InvisibleButton(id, size);
		var min = ImGui.GetItemRectMin();
		var max = ImGui.GetItemRectMax();
		var draw = ImGui.GetWindowDrawList();
		var hovered = ImGui.IsItemHovered();
		var active = ImGui.IsItemActive();
		var style = ImGui.GetStyle();
		var bg = active
			? style.Colors[(int)ImGuiCol.ButtonActive]
			: hovered
				? style.Colors[(int)ImGuiCol.ButtonHovered]
				: style.Colors[(int)ImGuiCol.Button];
		var border = selected
			? selectedColor ?? style.Colors[(int)ImGuiCol.CheckMark]
			: new Vector4(0.18f, 0.18f, 0.20f, 1.0f);

		draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(bg), style.FrameRounding);
		var padding = imagePadding ?? style.FramePadding;
		var imageMin = min + padding;
		var imageMax = max - padding;
		if (fitAspectRatio && subtexture.Width > 0 && subtexture.Height > 0)
		{
			var imageAvailable = imageMax - imageMin;
			var scale = MathF.Min(imageAvailable.X / subtexture.Width, imageAvailable.Y / subtexture.Height);
			var frameSize = subtexture.Size * scale;
			var frameMin = imageMin + (imageAvailable - frameSize) * 0.5f;
			imageMin = frameMin + subtexture.DrawCoords[0] * scale;
			imageMax = frameMin + subtexture.DrawCoords[2] * scale;
		}
		draw.AddImage(
			GetTextureID(subtexture.Texture),
			imageMin,
			imageMax,
			subtexture.TexCoords[0],
			subtexture.TexCoords[2]);
		draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(border), style.FrameRounding);
		return pressed;
	}

	public void Image(in Subtexture subtexture, Vector2 size, bool flipY = false)
	{
		if (subtexture.Texture == null)
			return;

		ImGui.Dummy(size);

		var min = ImGui.GetItemRectMin();
		var max = ImGui.GetItemRectMax();
		var draw = ImGui.GetWindowDrawList();
		var scale = new Vector2(
			subtexture.Width > 0 ? size.X / subtexture.Width : 1,
			subtexture.Height > 0 ? size.Y / subtexture.Height : 1);
		var imageMin = min + subtexture.DrawCoords[0] * scale;
		var imageMax = min + subtexture.DrawCoords[2] * scale;

		var uv_min = subtexture.TexCoords[0];
		var uv_max = subtexture.TexCoords[2];

		if (flipY)
		{
			uv_min.Y = 1.0f - uv_min.Y;
			uv_max.Y = 1.0f - uv_max.Y;
		}

		draw.AddImage(
			GetTextureID(subtexture.Texture),
			imageMin,
			imageMax,
			uv_min,
			uv_max);
	}

	public void Dispose()
	{
		GC.SuppressFinalize(this);
		ImGui.DestroyContext(context);
	}
}
