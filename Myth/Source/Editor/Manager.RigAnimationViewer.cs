#if EDITOR
using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;
using Kit;

namespace Myth;

public partial class Manager
{
	bool rigAnimationViewerOpen = false;
	bool rigAnimationViewerInitialized;
	string rigAnimationSearch = "";
	RigActor? rigAnimationPreviewActor;
	Pawn? rigPreviewPawn;
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
	RigHand rigTakeNearHand;
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
		var selected = SelectedRigActor();
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
		if (rigPreviewPawn != null && SelectedRigPawn() != rigPreviewPawn)
		{
			rigPreviewPawn.PreviewRigTake(null);
			rigPreviewPawn = null;
		}

		if (visible)
		{
			if (rigAnimationPreviewActor is { } rig)
				DrawRigAnimationControls(rig);
			else if (SelectedRigPawn() is { } pawn)
				DrawPawnRigTakeControls(pawn);
			else
			{
				ImGui.TextWrapped("Select a rig puppet or prop in the game view, or a rig-drawn pawn while playing to tune its takes live.");
				ImGui.Spacing();
				ImGui.TextDisabled("The preview runs on the live actor so projection, lighting, IK, and blending match the game.");
			}
		}
		ImGui.End();

		if (!rigAnimationViewerOpen)
			StopRigAnimationPreview();
	}

	RigActor? SelectedRigActor()
	{
		if (game?.CurrentRoom is not { } room)
			return null;
		if (playing)
			return TryGetSelectedLiveActor(game, room, out var actor, out _, out _) ? actor as RigActor : null;
		if (CurrentRoom is not { } authoredRoom || authoredRoom.Id != room.Id || selectedRoomActorIndex < 0 ||
			selectedRoomActorIndex >= authoredRoom.Actors.Count ||
			ActorSpecs.GetActorId(authoredRoom.Actors[selectedRoomActorIndex]) is not { } id)
			return null;
		return game.GetActorById(id) as RigActor;
	}

	// The player is spawned, not authored, so it's inspected by reference rather than through its room spec.
	Pawn? SelectedRigPawn()
		=> playing && game is { } g &&
			(liveInspectedSpawned ?? (liveSelectedActorId is { } id ? g.GetActorById(id) : null)) is Pawn { RigVisual: not null } pawn &&
			g.Actors.Contains(pawn) && !g.destroying.Contains(pawn) ? pawn : null;

	// Tunes the takes a pawn plays during normal gameplay: edits apply live (RigTakes.Set) until saved.
	void DrawPawnRigTakeControls(Pawn pawn)
	{
		var playingName = pawn.RigPlaying;
		ImGui.TextUnformatted($"Playing: {playingName}");
		// Raw clips (idle, walk, jog...) edit as a new take of the same name, which overrides the clip once set.
		var isTake = RigTakes.TryGet(playingName, out var current);
		OzzRig.Clip? raw = null;
		if (playingName != rigTakeName && (isTake || pawn.RigVisual!.TryGetClip(playingName, out raw)))
		{
			ImGui.SameLine();
			if (ImGui.SmallButton("Edit"))
			{
				if (isTake)
					LoadRigTakeDraft(null, current);
				else
				{
					rigTakeDraftClip = null;
					SyncRigTakeDraft(raw!);
				}
			}
		}
		if (rigTakeDraftClip is { } clipName && pawn.RigVisual!.TryGetClip(clipName, out var clip))
		{
			if (ImGui.Button(pawn.RigPreview == rigTakeName ? "Replay on pawn" : "Preview on pawn"))
			{
				RigTakes.Set(RigTakeDraft());
				pawn.PreviewRigTake(rigTakeName);
				rigPreviewPawn = pawn;
			}
			if (pawn.RigPreview != null)
			{
				ImGui.SameLine();
				if (ImGui.Button("Stop preview"))
					pawn.PreviewRigTake(null);
			}
			DrawRigTakeControls(null, clip);
		}
		else
		{
			ImGui.TextDisabled("Play a take (roll, skid, climb...) and hit Edit, or pick one:");
			DrawSavedTakeCombo(null);
		}
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
		rigPreviewPawn?.PreviewRigTake(null);
		rigPreviewPawn = null;
		DisposeRigTakeThumbs();
	}

	void DrawRigAnimationControls(RigActor rig)
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
		var playbackSpeed = rig.AnimationPreviewSpeed;
		if (ImGui.SliderFloat("Speed", ref playbackSpeed, 0.1f, 3, "%.2fx"))
			rig.AnimationPreviewSpeed = playbackSpeed;
		var visualFps = rig.Rig.VisualFPS;
		if (ImGui.SliderFloat("Visual FPS", ref visualFps, 1, 240, "%.0f"))
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

	// The saved takes as a grid of animated thumbnails, drawn by the selected rig's own settings on a bone body.
	void DrawSavedTakeCombo(RigActor? rig)
	{
		if (ImGui.Button($"{(rigTakeName.Length > 0 ? rigTakeName : "(none)")}##SavedTake", new Vector2(ImGui.CalcItemWidth(), 0)))
			ImGui.OpenPopup("##RigTakeGrid");
		ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
		ImGui.TextUnformatted("Saved take");
		if (!ImGui.BeginPopup("##RigTakeGrid"))
		{
			DisposeRigTakeThumbs();
			return;
		}
		if ((rig?.Rig ?? SelectedRigPawn()?.RigVisual?.Settings) is not { } settings)
		{
			ImGui.EndPopup();
			return;
		}
		if (settings != rigThumbSource)
		{
			DisposeRigTakeThumbs();
			rigThumbSource = settings;
		}
		ImGui.SetNextItemWidth(RigTakeThumbColumns * (RigTakeThumbSize + ImGui.GetStyle().ItemSpacing.X));
		ImGui.InputTextWithHint("##RigTakeSearch", "Search takes", ref rigTakeThumbSearch, 128);

		var size = new Vector2(RigTakeThumbSize);
		var draw = ImGui.GetWindowDrawList();
		var slot = 0;
		var column = 0;
		var hovered = (string?)null;
		rigThumbShown.Clear();
		foreach (var take in RigTakes.All)
		{
			var at = slot++;
			if (rigTakeThumbSearch.Length > 0 && !take.Name.Contains(rigTakeThumbSearch, StringComparison.OrdinalIgnoreCase) &&
				!take.Clip.Contains(rigTakeThumbSearch, StringComparison.OrdinalIgnoreCase))
				continue;
			SwatchGridWrap(ref column, RigTakeThumbColumns);
			var tile = new Rect(at % RigTakeThumbColumns * RigTakeThumbSize, at / RigTakeThumbColumns * RigTakeThumbSize, size.X, size.Y);
			if (ImGuiSubtextureButton($"##take{take.Name}", new Subtexture(rigThumbAtlas?.Attachments[0], tile), $"{take.Name}  ({take.Clip})",
				take.Name == rigTakeName, size, 0f))
			{
				LoadRigTakeDraft(rig, take);
				ImGui.CloseCurrentPopup();
			}
			if (ImGui.IsItemHovered())
				hovered = take.Name;
			if (ImGui.IsItemVisible())
				rigThumbShown.Add((take, tile));
			draw.AddText(ImGui.GetItemRectMin() + new Vector2(3, 1),
				ImGui.GetColorU32(ImGuiCol.Text), take.Name);
		}
		ImGui.EndPopup();
		UpdateRigTakeThumbs(settings, slot, hovered);
	}

	const int RigTakeThumbSize = 96;
	const int RigTakeThumbColumns = 6;
	// Thumbnails pose at this rate, each on its own phase, so only a few pose each frame; the hovered one every frame.
	static float RigTakeThumbFPS => 12;
	static int RigTakeThumbsCreatedPerFrame => 3;
	string rigTakeThumbSearch = "";
	readonly Dictionary<string, (HumanoidRigVisual Visual, RigTake Take)> rigThumbs = [];
	readonly List<(RigTake Take, Rect Tile)> rigThumbShown = [];
	HumanoidRigSettings? rigThumbSource;
	Game? rigThumbGame;
	Actor? rigThumbOwner;
	Target? rigThumbAtlas;
	Batcher? rigThumbBatcher;

	void UpdateRigTakeThumbs(HumanoidRigSettings source, int count, string? hovered)
	{
		var rows = (count + RigTakeThumbColumns - 1) / RigTakeThumbColumns;
		if (rigThumbAtlas == null || rigThumbAtlas.Height != rows * RigTakeThumbSize)
		{
			rigThumbAtlas?.Dispose();
			rigThumbAtlas = new Target(GraphicsDevice, RigTakeThumbColumns * RigTakeThumbSize, Math.Max(1, rows) * RigTakeThumbSize,
				name: "RigTakeThumbs");
		}
		rigThumbGame ??= new Game(this, new Room(Point2.Zero, new Point2(Game.Columns, Game.Rows)),
			audioEventsEnabled: false, spawnBud: false, enableParticles: false);
		rigThumbOwner ??= rigThumbGame.Create<Actor>(Point2.Zero);
		rigThumbBatcher ??= new Batcher(GraphicsDevice, "RigTakeThumbs");

		// New thumbnails trickle in a few a frame, which also staggers their posing phases.
		var created = 0;
		foreach (var (take, tile) in rigThumbShown)
		{
			if (!rigThumbs.TryGetValue(take.Name, out var thumb))
			{
				if (created++ >= RigTakeThumbsCreatedPerFrame)
					continue;
				var settings = source.Copy();
				settings.BodyStyle = RigBodyStyle.Bones;
				settings.KeyEvents = false;
				settings.PixelsPerMeter = RigTakeThumbSize * 0.4f;
				thumb.Visual = new(rigThumbOwner, settings);
				thumb.Visual.Initialize(take.Clip);
			}
			if (thumb.Take != take)
			{
				thumb.Visual.Play(take);
				thumb = (thumb.Visual, take);
				rigThumbs[take.Name] = thumb;
			}
			thumb.Visual.Settings.VisualFPS = take.Name == hovered ? 240 : RigTakeThumbFPS;
			thumb.Visual.Update(grounded: true, footIK: false);
			rigThumbBatcher.PushMatrix(Matrix3x2.CreateTranslation(tile.Center + new Vector2(0, RigTakeThumbSize * 0.42f)));
			thumb.Visual.Draw(rigThumbBatcher, Color.White, silhouette: false);
			rigThumbBatcher.PopMatrix();
		}
		rigThumbAtlas.Clear(Color.Transparent);
		rigThumbBatcher.Render(rigThumbAtlas);
		rigThumbBatcher.Clear();
	}

	void DisposeRigTakeThumbs()
	{
		foreach (var (visual, _) in rigThumbs.Values)
			visual.Dispose();
		rigThumbs.Clear();
		rigThumbSource = null;
		rigThumbAtlas?.Dispose();
		rigThumbAtlas = null;
		rigThumbBatcher?.Dispose();
		rigThumbBatcher = null;
		rigThumbOwner = null;
		rigThumbGame?.Dispose();
		rigThumbGame = null;
	}

	// rig == null tunes the take live for whoever plays it (a pawn in gameplay) instead of previewing on a puppet.
	void DrawRigTakeControls(RigActor? rig, OzzRig.Clip clip)
	{
		ImGui.SeparatorText("Take");
		DrawSavedTakeCombo(rig);
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
			if (rig != null)
				rig.AnimationPreviewSpeed = speed;
			else
				RigTakes.Set(RigTakeDraft());
		}
		if (ImGui.Checkbox("Take loop", ref rigTakeLoop))
		{
			if (rig != null)
				rig.AnimationPreviewLooping = rigTakeLoop;
			else
				RigTakes.Set(RigTakeDraft());
		}
		ImGui.SameLine();
		if (ImGui.Checkbox("Take foot IK", ref rigTakeFootIK))
		{
			if (rig != null)
				rig.SetAnimationPreviewIK(rigTakeFootIK);
			else
				RigTakes.Set(RigTakeDraft());
		}

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
		// The hand kept toward the viewer: the take plays mirrored whenever it would be the far one.
		var nearHand = (int)rigTakeNearHand;
		if (ImGui.Combo("Near hand", ref nearHand, "None\0Left\0Right\0"))
		{
			rigTakeNearHand = (RigHand)nearHand;
			PreviewRigTakeDraft(rig);
		}

		if (rig != null && ImGui.Button("Preview take"))
			PreviewRigTakeDraft(rig);
		if (rig != null)
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
		if (rig == null)
		{
			if (rigTakeStatus.Length > 0)
				ImGui.TextDisabled(rigTakeStatus);
			else
				ImGui.TextDisabled("Edits play live; Save take to keep them.");
			return;
		}
		ImGui.SameLine();
		if (ImGui.Button("Copy call"))
		{
			Input.SetClipboardString($"rig.PlayTake({System.Text.Json.JsonSerializer.Serialize(rigTakeName)}, blendDuration: 0.2f);");
			rigTakeStatus = "Copied PlayTake call.";
		}
		if (rigTakeStatus.Length > 0)
			ImGui.TextDisabled(rigTakeStatus);

		if (rig is not RigPuppet puppet)
			return;
		ImGui.SeparatorText("Sequence test");
		ImGui.SetNextItemWidth(-1);
		ImGui.InputText("Line", ref rigGreetingText, 512);
		if (ImGui.Button("Walk, kneel, and speak"))
		{
			StopRigAnimationPreview();
			rigAnimationViewerOpen = false;
			puppet.GreetPlayer(rigGreetingText);
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
		rigTakeNearHand = RigHand.None;
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
			rigTakeNearHand = saved.NearHand;
		}
	}

	void LoadRigTakeDraft(RigActor? rig, RigTake take)
	{
		rigTakeDraftClip = take.Clip;
		rigTakeName = take.Name;
		rigTakeStart = take.Start;
		rigTakeEnd = take.End;
		rigTakeSpeed = take.Speed;
		rigTakeUpperBodyLeanDegrees = take.UpperBodyLeanDegrees;
		rigTakeArmWidthScale = take.ArmWidthScale;
		rigTakeHandWidthScale = take.HandWidthScale;
		rigTakeNearHand = take.NearHand;
		rigTakeLoop = take.Loop;
		rigTakeFootIK = take.FootIK;
		rigTakeStatus = "";
		rig?.PreviewTake(take);
	}

	void PreviewRigTakeDraft(RigActor? rig)
	{
		if (rig != null)
			rig.PreviewTake(RigTakeDraft());
		else
			RigTakes.Set(RigTakeDraft());
	}

	RigTake RigTakeDraft()
		=> new(rigTakeName, rigTakeDraftClip!, rigTakeStart, rigTakeEnd,
			rigTakeLoop, rigTakeSpeed, rigTakeFootIK, rigTakeUpperBodyLeanDegrees,
			rigTakeArmWidthScale, rigTakeHandWidthScale, rigTakeNearHand);
}
#endif
