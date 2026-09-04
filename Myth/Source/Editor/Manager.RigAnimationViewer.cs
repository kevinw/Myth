#if EDITOR
using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using ImGuiNET;
using Kit;

namespace Myth;

public partial class Manager
{
	bool rigAnimationViewerOpen = false;
	bool rigAnimationViewerInitialized;
	string rigAnimationSearch = "";
	RigPuppet? rigAnimationPreviewActor;
	string? rigTakeDraftClip;
	string rigTakeName = "";
	string rigTakeStatus = "";
	string rigGreetingText = "I have been looking for you.";
	float rigTakeStart;
	float rigTakeEnd;
	float rigTakeSpeed = 1;
	float rigTakeUpperBodyLeanDegrees;
	float rigTakeArmWidthScale = 1;
	float rigTakeHandWidthScale = 1;
	bool rigTakeLoop;
	bool rigTakeFootIK;

	void RigAnimationViewerImGui()
	{
		if (!rigAnimationViewerInitialized)
		{
			rigAnimationViewerInitialized = true;
			rigAnimationViewerOpen = false;
		}
		if (!rigAnimationViewerOpen)
		{
			StopRigAnimationPreview();
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(380, 430), ImGuiCond.FirstUseEver);
		var visible = ImGui.Begin("Rig Animations", ref rigAnimationViewerOpen, ImGuiWindowFlags.NoDocking);
		var selected = SelectedRigPuppet();
		var previewGone = rigAnimationPreviewActor != null &&
			(game == null || game.destroying.Contains(rigAnimationPreviewActor) || !game.Actors.Contains(rigAnimationPreviewActor));
		if (previewGone || rigAnimationPreviewActor != null && selected == null)
			StopRigAnimationPreview();
		else if (selected != null && selected != rigAnimationPreviewActor)
		{
			StopRigAnimationPreview();
			rigAnimationPreviewActor = selected;
			rigAnimationPreviewActor?.BeginAnimationPreview();
		}

		if (visible)
		{
			if (rigAnimationPreviewActor is { } rig)
				DrawRigAnimationControls(rig);
			else
			{
				ImGui.TextWrapped("Select a RigPuppet in the game view.");
				ImGui.Spacing();
				ImGui.TextDisabled("The preview runs on the live actor so projection, lighting, IK, and blending match the game.");
			}
		}
		ImGui.End();

		if (!rigAnimationViewerOpen)
			StopRigAnimationPreview();
	}

	RigPuppet? SelectedRigPuppet()
	{
		if (game?.CurrentRoom is not { } room)
			return null;
		if (playing)
			return TryGetSelectedLiveActor(game, room, out var actor, out _, out _) ? actor as RigPuppet : null;
		if (CurrentRoom is not { } authoredRoom || authoredRoom.Id != room.Id || selectedRoomActorIndex < 0 ||
			selectedRoomActorIndex >= authoredRoom.Actors.Count ||
			ActorSpecs.GetActorId(authoredRoom.Actors[selectedRoomActorIndex]) is not { } id)
			return null;
		return game.GetActorById(id) as RigPuppet;
	}

	bool UpdateRigAnimationPreviewInEditor()
	{
		if (playing || rigAnimationPreviewActor is not { AnimationPreviewEnabled: true } rig)
			return false;
		rig.UpdateAnimationPreviewInEditor();
		return true;
	}

	void StopRigAnimationPreview()
	{
		rigAnimationPreviewActor?.EndAnimationPreview();
		rigAnimationPreviewActor = null;
	}

	void DrawRigAnimationControls(RigPuppet rig)
	{
		var clips = rig.AnimationClips;
		var clip = rig.AnimationPreviewClip;
		SyncRigTakeDraft(clip);
		ImGui.TextUnformatted($"{rig.AnimationPreviewClipIndex + 1} / {clips.Count}");
		ImGui.SameLine();
		ImGui.TextDisabled($"{clip.Duration:0.###} s  •  compressed ozz clip");

		ImGui.SetNextItemWidth(-1);
		if (ImGui.BeginCombo("##RigAnimationClip", clip.Name))
		{
			ImGui.SetNextItemWidth(-1);
			ImGui.InputTextWithHint("##RigAnimationSearch", "Search clips", ref rigAnimationSearch, 128);
			ImGui.Separator();
			for (var i = 0; i < clips.Count; i++)
			{
				var candidate = clips[i];
				if (rigAnimationSearch.Length > 0 &&
					!candidate.Name.Contains(rigAnimationSearch, StringComparison.OrdinalIgnoreCase))
					continue;
				var selected = i == rig.AnimationPreviewClipIndex;
				if (ImGui.Selectable(candidate.Name, selected))
				{
					rig.PreviewAnimation(i);
					rigTakeDraftClip = null;
				}
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}

		if (ImGui.Button("Previous"))
			rig.PreviewAnimation((rig.AnimationPreviewClipIndex + clips.Count - 1) % clips.Count);
		ImGui.SameLine();
		if (ImGui.Button("Restart"))
			rig.AnimationPreviewTime = rig.AnimationPreviewStart;
		ImGui.SameLine();
		if (ImGui.Button("Next"))
			rig.PreviewAnimation((rig.AnimationPreviewClipIndex + 1) % clips.Count);

		var paused = rig.AnimationPreviewPaused;
		if (ImGui.Checkbox("Paused", ref paused))
			rig.AnimationPreviewPaused = paused;
		ImGui.SameLine();
		var looping = rig.AnimationPreviewLooping;
		if (ImGui.Checkbox("Loop", ref looping))
			rig.AnimationPreviewLooping = looping;

		var time = rig.AnimationPreviewTime;
		ImGui.SetNextItemWidth(-1);
		if (ImGui.SliderFloat("##RigAnimationTime", ref time,
			rig.AnimationPreviewStart, rig.AnimationPreviewEnd, "%.3f s"))
			rig.AnimationPreviewTime = time;
		if (rig is RigPuppetProp prop)
		{
			if (ImGui.Button("Use current frame as pose"))
				SaveRigPuppetPropPose(prop);
			ImGui.SameLine();
			if (ImGui.Button(prop.PoseAnimationName.StartsWith("Pose_", StringComparison.Ordinal)
				? "Edit in Blender" : "New Blender pose"))
				OpenRigPuppetPropPoseInBlender(prop);
		}

		DrawRigTakeControls(rig, clip);

		ImGui.SeparatorText("Playback");
		var playbackSpeed = rig.PlaybackSpeed;
		if (ImGui.SliderFloat("Speed", ref playbackSpeed, 0.1f, 3, "%.2fx"))
			rig.PlaybackSpeed = playbackSpeed;
		var visualFps = rig.Rig.VisualFPS;
		if (ImGui.SliderFloat("Visual FPS", ref visualFps, 1, 60, "%.0f"))
			rig.Rig.VisualFPS = visualFps;

		ImGui.SeparatorText("Procedural layers");
		var blend = rig.AnimationPreviewBlend;
		if (ImGui.Checkbox("Blend between clips", ref blend))
			rig.AnimationPreviewBlend = blend;
		var ik = rig.AnimationPreviewIK;
		if (ImGui.Checkbox("Foot IK", ref ik))
			rig.SetAnimationPreviewIK(ik);
	}

	void SaveRigPuppetPropPose(RigPuppetProp prop)
	{
		var clip = prop.AnimationPreviewClip;
		var progress = clip.Duration > 0 ? prop.AnimationPreviewTime / clip.Duration : 0;
		prop.PoseAnimationName = clip.Name;
		prop.PoseProgress = Math.Clamp(progress, 0, 1);
		if (game?.CurrentRoom is not { } runtimeRoom ||
			!TryGetLiveActorSource(game, runtimeRoom, prop, positionEditableOnly: false,
				out var room, out var authored))
			return;

		using (CaptureRoomUndo("Set Rig Puppet Prop Pose", room))
		{
			ActorSpecs.SetProperty(authored, nameof(RigPuppetProp.PoseAnimationName),
				JsonValue.Create(prop.PoseAnimationName));
			ActorSpecs.SetProperty(authored, nameof(RigPuppetProp.PoseProgress),
				JsonValue.Create(prop.PoseProgress));
		}
		room.Save();
		MarkRoomPreviewDirty(room);
		editorGameDirty = true;
		DisplayMessage($"Pose set to {clip.Name} at {prop.PoseProgress:P0}");
	}

	void OpenRigPuppetPropPoseInBlender(RigPuppetProp prop)
	{
		var action = prop.PoseAnimationName.StartsWith("Pose_", StringComparison.Ordinal)
			? prop.PoseAnimationName
			: $"Pose_{prop.ActorId ?? "Statue"}";
		var blend = Path.GetFullPath(Path.Join(Assets.AssetsPath, "Sources", "Models", "UAL2_Standard.blend"));
		var script = Path.GetFullPath(Path.Join(Assets.AssetsPath, "..", "tools", "rig-baker", "edit-pose.py"));
		var macBlender = "/Applications/Blender.app/Contents/MacOS/Blender";
		var info = new ProcessStartInfo(File.Exists(macBlender) ? macBlender : "blender")
		{
			UseShellExecute = false,
		};
		info.ArgumentList.Add(blend);
		info.ArgumentList.Add("--python");
		info.ArgumentList.Add(script);
		info.ArgumentList.Add("--");
		info.ArgumentList.Add("--action");
		info.ArgumentList.Add(action);
		try
		{
			Process.Start(info);
			rigTakeStatus = $"Opened {action}. Save in Blender to bake it, then select it here.";
		}
		catch (Exception e)
		{
			rigTakeStatus = $"Unable to open Blender: {e.Message}";
		}
	}

	void DrawRigTakeControls(RigPuppet rig, OzzRig.Clip clip)
	{
		ImGui.SeparatorText("Take");
		if (ImGui.BeginCombo("Saved take", rigTakeName))
		{
			foreach (var take in RigTakes.All)
				if (ImGui.Selectable($"{take.Name}  ({take.Clip})"))
					LoadRigTakeDraft(rig, take);
			ImGui.EndCombo();
		}
		ImGui.SetNextItemWidth(-1);
		ImGui.InputText("Name", ref rigTakeName, 128);

		var start = rigTakeStart;
		var end = rigTakeEnd;
		if (ImGui.DragFloatRange2("Range", ref start, ref end, 1f / 30f, 0, clip.Duration, "%.3f", "%.3f"))
		{
			rigTakeStart = Math.Clamp(start, 0, clip.Duration);
			rigTakeEnd = Math.Clamp(end, rigTakeStart, clip.Duration);
			PreviewRigTakeDraft(rig);
		}

		var speed = rigTakeSpeed;
		if (ImGui.SliderFloat("Take speed", ref speed, 0.1f, 3, "%.2fx"))
		{
			rigTakeSpeed = speed;
			rig.PlaybackSpeed = speed;
		}
		if (ImGui.Checkbox("Take loop", ref rigTakeLoop))
			rig.AnimationPreviewLooping = rigTakeLoop;
		ImGui.SameLine();
		if (ImGui.Checkbox("Take foot IK", ref rigTakeFootIK))
			rig.SetAnimationPreviewIK(rigTakeFootIK);

		var lean = rigTakeUpperBodyLeanDegrees;
		if (ImGui.SliderFloat("Upper-body lean", ref lean, -20, 20, "%.1f deg"))
		{
			rigTakeUpperBodyLeanDegrees = lean;
			PreviewRigTakeDraft(rig);
		}
		var armScale = rigTakeArmWidthScale;
		if (ImGui.SliderFloat("Arm width", ref armScale, 0.5f, 2, "%.2fx"))
		{
			rigTakeArmWidthScale = armScale;
			PreviewRigTakeDraft(rig);
		}
		var handScale = rigTakeHandWidthScale;
		if (ImGui.SliderFloat("Hand width", ref handScale, 0.5f, 2, "%.2fx"))
		{
			rigTakeHandWidthScale = handScale;
			PreviewRigTakeDraft(rig);
		}

		if (ImGui.Button("Preview take"))
			PreviewRigTakeDraft(rig);
		ImGui.SameLine();
		if (ImGui.Button("Save take"))
		{
			try
			{
				RigTakes.Save(RigTakeDraft());
				rigTakeStatus = $"Saved {rigTakeName}.";
			}
			catch (Exception e)
			{
				rigTakeStatus = e.Message;
			}
		}
		ImGui.SameLine();
		if (ImGui.Button("Copy call"))
		{
			Input.SetClipboardString($"rig.PlayTake({System.Text.Json.JsonSerializer.Serialize(rigTakeName)}, blendDuration: 0.2f);");
			rigTakeStatus = "Copied PlayTake call.";
		}
		if (rigTakeStatus.Length > 0)
			ImGui.TextDisabled(rigTakeStatus);

		ImGui.SeparatorText("Sequence test");
		ImGui.SetNextItemWidth(-1);
		ImGui.InputText("Line", ref rigGreetingText, 512);
		if (ImGui.Button("Walk, kneel, and speak"))
		{
			StopRigAnimationPreview();
			rigAnimationViewerOpen = false;
			rig.GreetPlayer(rigGreetingText);
		}
	}

	void SyncRigTakeDraft(OzzRig.Clip clip)
	{
		if (rigTakeDraftClip == clip.Name)
			return;
		rigTakeDraftClip = clip.Name;
		rigTakeName = clip.Name;
		rigTakeStart = 0;
		rigTakeEnd = clip.Duration;
		rigTakeSpeed = 1;
		rigTakeUpperBodyLeanDegrees = 0;
		rigTakeArmWidthScale = rigTakeHandWidthScale = 1;
		rigTakeLoop = clip.Name.EndsWith("_Loop", StringComparison.OrdinalIgnoreCase);
		rigTakeFootIK = false;
		rigTakeStatus = "";
		if (RigTakes.TryGet(clip.Name, out var saved))
		{
			rigTakeStart = saved.Start;
			rigTakeEnd = saved.End;
			rigTakeSpeed = saved.Speed;
			rigTakeLoop = saved.Loop;
			rigTakeFootIK = saved.FootIK;
			rigTakeUpperBodyLeanDegrees = saved.UpperBodyLeanDegrees;
			rigTakeArmWidthScale = saved.ArmWidthScale;
			rigTakeHandWidthScale = saved.HandWidthScale;
		}
	}

	void LoadRigTakeDraft(RigPuppet rig, RigTake take)
	{
		rigTakeDraftClip = take.Clip;
		rigTakeName = take.Name;
		rigTakeStart = take.Start;
		rigTakeEnd = take.End;
		rigTakeSpeed = take.Speed;
		rigTakeUpperBodyLeanDegrees = take.UpperBodyLeanDegrees;
		rigTakeArmWidthScale = take.ArmWidthScale;
		rigTakeHandWidthScale = take.HandWidthScale;
		rigTakeLoop = take.Loop;
		rigTakeFootIK = take.FootIK;
		rigTakeStatus = "";
		rig.PreviewTake(take);
	}

	void PreviewRigTakeDraft(RigPuppet rig) => rig.PreviewTake(RigTakeDraft());

	RigTake RigTakeDraft()
		=> new(rigTakeName, rigTakeDraftClip!, rigTakeStart, rigTakeEnd,
			rigTakeLoop, rigTakeSpeed, rigTakeFootIK, rigTakeUpperBodyLeanDegrees,
			rigTakeArmWidthScale, rigTakeHandWidthScale);
}
#endif
