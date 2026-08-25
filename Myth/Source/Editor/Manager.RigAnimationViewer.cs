#if EDITOR
using System.Numerics;
using ImGuiNET;

namespace Myth;

public partial class Manager
{
	bool rigAnimationViewerOpen = false;
	bool rigAnimationViewerInitialized;
	string rigAnimationSearch = "";
	RigPuppet? rigAnimationPreviewActor;

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
		if (!playing || previewGone || liveSelectedActorId != null && selected == null)
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
				ImGui.TextWrapped(playing
					? "Select a RigPuppet in the game view."
					: "Enter play mode, then select a RigPuppet in the game view.");
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
		if (!playing || game?.CurrentRoom is not { } room ||
			!TryGetSelectedLiveActor(game, room, out var actor, out _, out _))
			return null;
		return actor as RigPuppet;
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
					rig.PreviewAnimation(i);
				if (selected)
					ImGui.SetItemDefaultFocus();
			}
			ImGui.EndCombo();
		}

		if (ImGui.Button("Previous"))
			rig.PreviewAnimation((rig.AnimationPreviewClipIndex + clips.Count - 1) % clips.Count);
		ImGui.SameLine();
		if (ImGui.Button("Restart"))
			rig.AnimationPreviewTime = 0;
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
		if (ImGui.SliderFloat("##RigAnimationTime", ref time, 0, clip.Duration, "%.3f s"))
			rig.AnimationPreviewTime = time;

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
}
#endif
