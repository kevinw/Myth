#if EDITOR
using System.Numerics;
using System.Text.Json.Nodes;
using Foster.Framework;
using ImGuiNET;

namespace Myth;

/// Editor for the shared particle effect assets: the sliders in the actor inspector, the pinned
/// tuning window, drag-length undo scopes, and the debounced autosave. Owned by Manager (a plain
/// mutable field), which forwards the ImGui draw calls and the per-frame update here.
public struct ParticleEffectEditor(JsonUndoManager undo)
{
	const float AutosaveDelay = 0.6f;

	// Pending-scope slot (see JsonUndoManager.OpenPending): a slider drag spans many frames;
	// one scope is held open across the drag so it becomes a single undo entry, committed
	// when the widget deactivates. Keyed by effect name so switching effects commits first.
	const string UndoSlot = "particle-effect";

	// Pinned via the actor inspector's Pin button or the toolbar's Particles button; stays up
	// during play mode so effects can be tuned live. Session-only; the toolbar button reopens
	// the last pinned effect.
	string? pinnedEffect;
	string? lastPinnedEffect;

	// Debounced autosave for the effect asset being edited.
	string? autosaveName;
	float autosaveTimer;
	string? renameEffect;
	string renameName = "";
	string? assetActionError;
	bool renamePopupRequested;

	/// True while an edit is waiting on the debounced autosave (the asset file is stale).
	public readonly bool AutosavePending => autosaveName != null;

	public readonly bool PinnedWindowOpen => pinnedEffect != null;

	public void TogglePinnedWindow()
		=> pinnedEffect = pinnedEffect == null
			? lastPinnedEffect ?? ParticleEffectAssets.Names.FirstOrDefault()
			: null;

	public void Update(float deltaTime)
	{
		if (autosaveName is not { } name)
			return;
		autosaveTimer -= deltaTime;
		if (autosaveTimer <= 0)
		{
			ParticleEffectAssets.Save(name);
			autosaveName = null;
		}
	}

	// Draws the editable fields of a shared particle effect asset inside the
	// actor inspector. Each change is captured for undo (like actor properties)
	// and triggers a debounced autosave.
	public void DrawActorInspectorBox(Game? game, JsonObject actor)
	{
		if (game?.Particles is not { Supported: true })
			return;

		// EffectName is stripped from the JSON when it equals the default, so fall
		// back to the actor's baseline value (what the runtime actually uses).
		var name = ActorSpecs.ReadString(actor, "EffectName")
			?? ActorSpecs.GetEditableProperties(actor).FirstOrDefault(p => p.JsonName == "EffectName").Value?.GetValue<string>();
		if (name == null || !ParticleEffectAssets.Exists(name))
			return;

		ImGui.SeparatorText("Particle Asset");
		ImGui.TextDisabled($"shared '{name}' - autosaves");
		ImGui.SameLine();
		var pinned = pinnedEffect == name;
		if (ImGui.SmallButton(pinned ? "Unpin" : "Pin"))
			pinnedEffect = pinned ? null : name;
		ImGui.Indent();
		if (ImGui.Button("Test Burst", new Vector2(-1, 0)))
			TestBurst(game, actor);

		DrawControls(name);

		ImGui.Unindent();
	}

	public void PinnedWindowImGui(Game? game, float scale)
	{
		if (pinnedEffect is not { } name)
			return;
		if (!ParticleEffectAssets.Exists(name))
		{
			pinnedEffect = null;
			return;
		}
		lastPinnedEffect = name;

		var open = true;
		ImGui.SetNextWindowSize(new Vector2(300, 560) * scale, ImGuiCond.FirstUseEver);
		if (ImGui.Begin("Particles##Pinned", ref open, ImGuiWindowFlags.NoSavedSettings))
		{
			ImGui.SetNextItemWidth(-1);
			if (ImGui.BeginCombo("##PinnedEffectName", name))
			{
				foreach (var other in ParticleEffectAssets.Names)
					if (ImGui.Selectable(other, other == name))
						pinnedEffect = name = other;
				ImGui.EndCombo();
			}
			if (ImGui.Button("Duplicate"))
			{
				Flush(name);
				if (ParticleEffectAssets.TryDuplicate(name, out var copy, out assetActionError))
				{
					pinnedEffect = lastPinnedEffect = name = copy;
					game?.SetPreviewEffect(name);
				}
			}
			ImGui.SameLine();
			if (ImGui.Button("Rename..."))
			{
				Flush(name);
				renameEffect = name;
				renameName = name;
				assetActionError = null;
				renamePopupRequested = true;
			}
			if (assetActionError != null)
				ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.35f, 1.0f), assetActionError);
			if (ImGui.Button("Test Burst", new Vector2(-1, 0)) && game != null)
			{
				game.SetPreviewEffect(name);
				game.BurstTestEffect();
			}
			DrawControls(name);
			DrawRenamePopup(game);
		}
		ImGui.End();

		if (!open)
			pinnedEffect = null;
	}

	void DrawRenamePopup(Game? game)
	{
		if (renamePopupRequested)
		{
			renamePopupRequested = false;
			ImGui.OpenPopup("Rename Particle Effect");
		}

		var open = true;
		if (!ImGui.BeginPopupModal("Rename Particle Effect", ref open, ImGuiWindowFlags.AlwaysAutoResize))
			return;

		ImGui.TextUnformatted($"Rename '{renameEffect}':");
		ImGui.SetNextItemWidth(280);
		if (ImGui.IsWindowAppearing())
			ImGui.SetKeyboardFocusHere();
		var submit = ImGui.InputText("##ParticleEffectName", ref renameName, 128,
			ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
		ImGui.TextColored(new Vector4(1.0f, 0.72f, 0.3f, 1.0f),
			"Existing room and code references are not renamed.");

		var candidate = renameName.Trim();
		var valid = renameEffect != null && ParticleEffectAssets.IsValidName(candidate) &&
			!string.Equals(renameEffect, candidate, StringComparison.OrdinalIgnoreCase) &&
			!ParticleEffectAssets.Exists(candidate);
		ImGui.BeginDisabled(!valid);
		var rename = ImGui.Button("Rename", new Vector2(90, 0)) || submit;
		ImGui.EndDisabled();
		ImGui.SameLine();
		var cancel = ImGui.Button("Cancel", new Vector2(90, 0)) || !open;
		if (rename && valid && ParticleEffectAssets.TryRename(renameEffect!, candidate, out assetActionError))
		{
			pinnedEffect = lastPinnedEffect = candidate;
			game?.SetPreviewEffect(candidate);
			renameEffect = null;
			ImGui.CloseCurrentPopup();
		}
		if (assetActionError != null)
			ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.35f, 1.0f), assetActionError);
		if (cancel)
		{
			renameEffect = null;
			assetActionError = null;
			ImGui.CloseCurrentPopup();
		}
		ImGui.EndPopup();
	}

	// The shared effect-asset sliders, used by both the actor inspector and the
	// pinned play-mode window.
	void DrawControls(string name)
	{
		// Edits made inside a popup (the color picker) deactivate in the popup window, where
		// the IsItemDeactivatedAfterEdit checks below can't see it. Once nothing is active
		// anywhere, the drag is over: seal any pending scope.
		if (undo.HasPending(UndoSlot) && !ImGui.IsAnyItemActive())
			CommitUndo();

		var e = ParticleEffectAssets.Get(name);

		FxShape("Shape", name, ref e.Shape);
		FxInt("Count", name, ref e.BurstCount, 1, ParticleSystem.Capacity);
		if (e.Shape == ParticleEmitterShape.Circle)
			FxFloat("Radius", name, ref e.EmitterRadius, 0.0f, 320.0f);
		if (e.Shape == ParticleEmitterShape.Cone)
		{
			FxFloat("Direction", name, ref e.DirectionDegrees, -180.0f, 180.0f);
			FxFloat("Spread", name, ref e.SpreadDegrees, 0.0f, 360.0f);
		}
		if (e.Shape == ParticleEmitterShape.Line)
		{
			FxFloat("Length", name, ref e.EmitterRadius, 0.0f, 128.0f);
			FxFloat("Direction", name, ref e.DirectionDegrees, -180.0f, 180.0f);
			FxFloat("Thickness", name, ref e.SpreadDegrees, 0.0f, 32.0f);
		}
		FxMode("Mode", name, ref e.Mode);
		FxCheckbox("Prewarm", name, ref e.Prewarm);
		if (e.Mode == ParticleEmissionMode.Continuous)
			FxFloat("Rate", name, ref e.EmissionRate, 0.0f, 400.0f);

		ImGui.SeparatorText("Dynamics");
		FxRange("Speed", name, ref e.SpeedMin, ref e.SpeedMax, 1.0f, 0.0f, 600.0f);
		FxRange("Life", name, ref e.LifeMin, ref e.LifeMax, 0.02f, 0.05f, 8.0f);
		FxFloat("Gravity", name, ref e.Gravity, -200.0f, 200.0f);
		FxFloat("Drag", name, ref e.Drag, 0.0f, 4.0f);
		FxFloat("Noise Amount", name, ref e.NoiseAmount, 0.0f, 200.0f);
		FxFloat("Noise Scale", name, ref e.NoiseScale, 0.005f, 0.3f);

		ImGui.SeparatorText("Appearance");
		FxFloat("Size Start", name, ref e.SizeStart, 0.0f, 16.0f);
		FxFloat("Size End", name, ref e.SizeEnd, 0.0f, 16.0f);
		FxCheckbox("Additive", name, ref e.Additive);
		FxStyle("Style", name, ref e.Style);
		FxColor("Birth", name, ref e.ColorBirth);
		FxColor("Mid", name, ref e.ColorMid);
		FxColor("Death", name, ref e.ColorEnd);

		ImGui.SeparatorText("Trail");
		FxFloat("Trail Seconds", name, ref e.TrailSeconds, 0.0f, 2.0f);
		if (e.TrailSeconds > 0.0f)
		{
			FxFloat("Trail Width Start", name, ref e.TrailWidthStart, 0.0f, 8.0f);
			FxFloat("Trail Width End", name, ref e.TrailWidthEnd, 0.0f, 8.0f);
			ImGui.TextDisabled("trail colors: TrailColors keys in the json");
		}

		ImGui.SeparatorText("Edge (sprite emission)");
		FxEdgeSide("Side", name, ref e.EdgeSide);
		if (e.EdgeSide == ParticleEdgeSide.FixedDirection)
			FxFloat("Side Angle", name, ref e.EdgeSideDegrees, -180.0f, 180.0f);
		if (e.EdgeSide != ParticleEdgeSide.All)
			FxFloat("Arc", name, ref e.EdgeArcDegrees, 10.0f, 360.0f);
		FxFloat("Attract", name, ref e.EdgeAttraction, 0.0f, 100.0f);
		FxFloat("Flow Along", name, ref e.EdgeFlow, -200.0f, 200.0f);

		ImGui.SeparatorText("Collision");
		FxCollision("Collision", name, ref e.Collision);
		if (e.Collision == ParticleCollisionMode.Bounce)
			FxFloat("Bounciness", name, ref e.Bounciness, 0.0f, 1.0f);
	}

	static void TestBurst(Game game, JsonObject actor)
	{
		var id = ActorSpecs.GetActorId(actor);
		if (id == null)
			return;
		foreach (var instance in game.Actors)
			if (instance is ParticleEmitter emitter && emitter.ActorId == id)
			{
				emitter.TestBurst();
				return;
			}
	}

	void MarkDirty(string name)
	{
		autosaveName = name;
		autosaveTimer = AutosaveDelay;
	}

	void Flush(string name)
	{
		CommitUndo();
		if (autosaveName != name)
			return;
		ParticleEffectAssets.Save(name);
		autosaveName = null;
	}

	JsonUndoScope CaptureUndo(string effectName)
		=> undo.Capture("Edit Particle Effect", new UndoTarget(effectName));

	void BeginUndo(string name)
	{
		if (!undo.HasPending(UndoSlot, name))
			undo.OpenPending(UndoSlot, name, "Edit Particle Effect", new UndoTarget(name));
	}

	void CommitUndo()
		=> undo.CommitPending(UndoSlot);

	void FxFloat(string label, string name, ref float field, float min, float max)
	{
		var v = field;
		if (ImGui.SliderFloat(label, ref v, min, max))
		{
			BeginUndo(name);
			field = v;
			MarkDirty(name);
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
			CommitUndo();
	}

	void FxInt(string label, string name, ref int field, int min, int max)
	{
		var v = field;
		if (ImGui.SliderInt(label, ref v, min, max))
		{
			BeginUndo(name);
			field = v;
			MarkDirty(name);
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
			CommitUndo();
	}

	void FxRange(string label, string name, ref float a, ref float b, float speed, float min, float max)
	{
		float x = a, y = b;
		if (ImGui.DragFloatRange2(label, ref x, ref y, speed, min, max))
		{
			BeginUndo(name);
			a = x;
			b = y;
			MarkDirty(name);
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
			CommitUndo();
	}

	void FxCheckbox(string label, string name, ref bool field)
	{
		var v = field;
		if (!ImGui.Checkbox(label, ref v))
			return;
		using (CaptureUndo(name)) field = v;
		MarkDirty(name);
	}

	void FxColor(string label, string name, ref Color field)
	{
		var v = (Vector4)field;
		if (ImGui.ColorEdit4(label, ref v, ImGuiColorEditFlags.AlphaBar))
		{
			BeginUndo(name);
			field = new Color(v);
			MarkDirty(name);
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
			CommitUndo();
	}

	void FxShape(string label, string name, ref ParticleEmitterShape field)
	{
		var v = (int)field;
		ImGui.SetNextItemWidth(-1);
		if (!ImGui.Combo($"##{label}", ref v, "Point\0Circle\0Cone\0Line\0"))
			return;
		using (CaptureUndo(name)) field = (ParticleEmitterShape)v;
		MarkDirty(name);
	}

	void FxStyle(string label, string name, ref ParticleRenderStyle field)
	{
		var v = (int)field;
		ImGui.SetNextItemWidth(-1);
		if (!ImGui.Combo($"##{label}", ref v, "Square\0Plasma\0"))
			return;
		using (CaptureUndo(name)) field = (ParticleRenderStyle)v;
		MarkDirty(name);
	}

	void FxMode(string label, string name, ref ParticleEmissionMode field)
	{
		var v = (int)field;
		ImGui.SetNextItemWidth(-1);
		if (!ImGui.Combo($"##{label}", ref v, "Continuous\0Burst\0"))
			return;
		using (CaptureUndo(name)) field = (ParticleEmissionMode)v;
		MarkDirty(name);
	}

	void FxEdgeSide(string label, string name, ref ParticleEdgeSide field)
	{
		var v = (int)field;
		ImGui.SetNextItemWidth(-1);
		if (!ImGui.Combo($"##{label}", ref v, "Facing Motion\0Fixed Direction\0All\0"))
			return;
		using (CaptureUndo(name)) field = (ParticleEdgeSide)v;
		MarkDirty(name);
	}

	void FxCollision(string label, string name, ref ParticleCollisionMode field)
	{
		var v = (int)field;
		ImGui.SetNextItemWidth(-1);
		if (!ImGui.Combo($"##{label}", ref v, "None\0Kill\0Settle\0Bounce\0"))
			return;
		using (CaptureUndo(name)) field = (ParticleCollisionMode)v;
		MarkDirty(name);
	}

	sealed class UndoTarget(string name) : IJsonUndoTarget
	{
		public string UndoId => $"particle-effect:{name}";
		public string CaptureJson() => ParticleEffectAssets.Get(name).ToJson().ToJsonString();
		public void ApplyJson(string json)
		{
			if (JsonNode.Parse(json) is JsonObject obj)
			{
				ParticleEffectAssets.Set(name, ParticleEffect.FromJson(obj));
				ParticleEffectAssets.Save(name);
			}
		}
	}
}
#endif
