using System.Numerics;
using Foster.Framework;
using static Myth.RigDraw;

namespace Myth;

partial class HumanoidRigVisual
{
	// The body as tapered bone quads in a depth-sorted list, shaded from the Near/Core/Far palette, with costume pieces,
	// a surface or NCA skin painted over the fills, and capsule combat bounds around the bones.
	sealed class BoneBody : IBody
	{
		readonly HumanoidRigVisual visual;
		readonly List<RigDrawItem> drawItems = [];
		readonly NeuralSkinRuntime neuralSkin;
		readonly BoneUV[] neuralBoneUVs;
		Vector2 neuralHeadUV;
		Vector2 neuralHeadUVRadius;
		readonly Vector2 surfaceMaterialOrigin;
		float neuralJostleAt;
		readonly float neuralJostlePhase;

		static float HandPalmRatio => 0.5f;
		static float HandFingerExtension => 0.38f;
		static float HandFingerRootSpread => 0.2f;
		static float HandFingerTipSpread => 0.38f;
		static float HandFingerWidth => 0.44f;
		static float HandFingerTipWidth => 0.22f;
		static float FingerSpinBlendSpeed => MathF.Tau;
		static bool DrawSpineToClavicle => false;
		static Point2 NeuralSize => new(64, 80);
		static Point2 NeuralOrigin => new(-32, -72);

		public BoneBody(HumanoidRigVisual visual)
		{
			this.visual = visual;
			var owner = visual.owner;
			neuralBoneUVs = new BoneUV[visual.bones.Length];
			CaptureNeuralUVs();
			surfaceMaterialOrigin = owner.Position;
			neuralJostlePhase = (owner.ActorId?.GetHashCode() ?? owner.Position.GetHashCode()) * 0.0001f;
			neuralJostleAt = owner.Time.SecondsF + visual.NeuralReactions.NeuralJostleInterval * 0.5f;
			neuralSkin = new(owner, visual.Neural, NeuralSize, NeuralOrigin, 65);
			neuralSkin.Initialize(visual.Settings.SkinMode);
		}

		public (Color Near, Color Core, Color Far) HeadPalette => (visual.Settings.NearColor, visual.Settings.CoreColor, visual.Settings.FarColor);

		public void Update() => neuralSkin.Update(visual.Settings.SkinMode);

		// Each pass rebuilds the rig's depth-ordered item list, then draws from it.
		public void Draw(Batcher batcher, Color tint, bool silhouette, DrawParts parts)
		{
			BuildDrawList();
			DrawItems(batcher, tint, silhouette, parts);
		}

		public void DrawNormals(Batcher batcher, DrawParts parts)
		{
			BuildDrawList();
			DrawItemNormals(batcher, parts);
		}

		public bool Overlaps(in Rect bounds, CombatRole role)
		{
			for (var i = 0; i < visual.bones.Length; i++)
			{
				var bone = visual.bones[i];
				if (!visual.BackArmCombatEnabled && bone.Arm == visual.backArm)
					continue;
				BoneWidths(bone, out var fromWidth, out var toWidth);
				if (bone.Combat && CapsuleOverlaps(bounds, visual.projectedPose[bone.From], visual.projectedPose[bone.To],
					MathF.Max(fromWidth, toWidth) * 0.5f + visual.Settings.CombatBoundsPadding))
					return true;
			}
			return false;
		}

		public void DrawCombatBounds(Batcher batcher, Color color)
		{
			for (var i = 0; i < visual.bones.Length; i++)
			{
				var bone = visual.bones[i];
				if (!bone.Combat || !visual.BackArmCombatEnabled && bone.Arm == visual.backArm)
					continue;
				var from = visual.owner.Position + visual.projectedPose[bone.From];
				var to = visual.owner.Position + visual.projectedPose[bone.To];
				BoneWidths(bone, out var fromWidth, out var toWidth);
				var radius = MathF.Max(fromWidth, toWidth) * 0.5f + visual.Settings.CombatBoundsPadding;
				batcher.Line(from, to, radius * 2, color * 0.2f);
				batcher.CircleLine(from, radius, 1, 10, color);
				batcher.CircleLine(to, radius, 1, 10, color);
			}
		}

		public Rect Bounds(Rect bounds)
		{
			for (var i = 0; i < visual.bones.Length; i++)
			{
				var bone = visual.bones[i];
				BoneWidths(bone, out var fromWidth, out var toWidth);
				BoneQuad(visual.projectedPose[bone.From], visual.projectedPose[bone.To], fromWidth + 2, toWidth + 2,
					out var a, out var b, out var c, out var d);
				bounds = bounds.Conflate(a).Conflate(b).Conflate(c).Conflate(d);
			}
			if (Assets.GetCostume(visual.Settings.CostumeId) != null)
			{
				BuildDrawList();
				foreach (var item in drawItems)
					if (item.Kind == RigDrawKind.Costume)
						bounds = bounds.Conflate(CostumeBounds(item));
			}
			return bounds;
		}

		public void Dispose() => neuralSkin.Dispose();

		Color BoneColor(Bone bone, float depth)
			=> bone.From == visual.pelvis || bone.To == visual.pelvis
				? visual.Settings.CoreColor
				: Color.Lerp(visual.Settings.FarColor, visual.Settings.NearColor, depth);

		// Costume shades with depth like the bone under it: the palette's far/near ratio at that depth, so far-side
		// clothing darkens and shifts hue as much as the limb does. Bones on the pelvis take the flat core color.
		Color CostumeShade(Bone bone)
		{
			if (bone.From == visual.pelvis || bone.To == visual.pelvis)
				return Color.White;
			static float Ratio(byte far, byte near) => near == 0 ? 1 : MathF.Min(1, far / (float)near);
			var near = visual.Settings.NearColor;
			var far = visual.Settings.FarColor;
			return Color.Lerp(new Color(Ratio(far.R, near.R), Ratio(far.G, near.G), Ratio(far.B, near.B), 1f), Color.White,
				BoneRenderDepth(bone));
		}

		float BoneWidth(Bone bone) => visual.Settings.PixelsPerMeter * bone.Part switch
		{
			Part.Torso => visual.Settings.TorsoSize,
			Part.Chest => visual.Settings.ChestSize,
			Part.Neck => visual.Settings.NeckSize,
			Part.Bicep => visual.Settings.BicepSize * visual.armWidthScale,
			Part.Forearm => visual.Settings.ForearmSize * visual.armWidthScale,
			Part.Hand => visual.Settings.ForearmSize * visual.handWidthScale *
				(!visual.scaleLeftHandOnly || bone.Arm == Side.Left ? visual.runtimeHandWidthScale : 1),
			Part.Thigh => visual.Settings.ThighSize,
			Part.Shin => visual.Settings.ShinSize,
			_ => visual.Settings.FootSize,
		};

		float BoneRenderDepth(Bone bone)
			=> Math.Clamp(visual.BoneDepth(bone) * MathF.Max(0, visual.Settings.DepthColorContrast) + 0.5f, 0, 1);

		void BoneWidths(Bone bone, out float fromWidth, out float toWidth)
		{
			fromWidth = BaseBoneWidth(bone);
			toWidth = TaperedWidth(bone, fromWidth);
			if (bone.Parent >= 0)
			{
				var parent = visual.bones[bone.Parent];
				fromWidth = float.Lerp(fromWidth, TaperedWidth(parent, BaseBoneWidth(parent)),
					Math.Clamp(visual.Settings.JointContinuity, 0, 1));
			}
		}

		float BaseBoneWidth(Bone bone)
			=> BoneWidth(bone) * (1 + MathF.Max(0, BoneRenderDepth(bone) - 0.5f) * 2 * visual.Settings.DepthWidthAmount);

		float TaperedWidth(Bone bone, float width) => width * (1 - Math.Clamp(
			bone.Part == Part.Chest ? visual.Settings.ChestTaper :
			bone.Part == Part.Neck ? visual.Settings.NeckTaper :
			bone.Leg != Side.None || bone.Part is Part.Bicep or Part.Forearm or Part.Hand ? visual.Settings.LimbTaper : 0, 0, 0.75f));

		void CaptureNeuralUVs()
		{
			for (var i = 0; i < visual.bones.Length; i++)
			{
				var bone = visual.bones[i];
				BoneWidths(bone, out var fromWidth, out var toWidth);
				BoneQuad(visual.projectedPose[bone.From], visual.projectedPose[bone.To], fromWidth, toWidth,
					out var a, out var b, out var c, out var d);
				neuralBoneUVs[i] = new(NeuralUV(a), NeuralUV(b), NeuralUV(c), NeuralUV(d));
			}
			neuralHeadUV = NeuralUV(visual.projectedPose[visual.head]);
			neuralHeadUVRadius = new Vector2(visual.HeadRadius) / (Vector2)NeuralSize;
		}

		static bool CapsuleOverlaps(in Rect bounds, Vector2 from, Vector2 to, float radius)
		{
			var segment = new Line(from, to);
			if (bounds.Overlaps(segment))
				return true;
			var radiusSquared = radius * radius;
			return segment.ClosestDistanceSquared(bounds.TopLine) <= radiusSquared ||
				segment.ClosestDistanceSquared(bounds.RightLine) <= radiusSquared ||
				segment.ClosestDistanceSquared(bounds.BottomLine) <= radiusSquared ||
				segment.ClosestDistanceSquared(bounds.LeftLine) <= radiusSquared;
		}

		void BuildDrawList()
		{
			drawItems.Clear();
			var accessoryIndex = 0;
			var headAdded = false;
			var headDepth = visual.jointDepth[visual.head];
			var costume = Assets.GetCostume(visual.Settings.CostumeId);
			Span<bool> added = stackalloc bool[visual.bones.Length];
			added.Clear();
			foreach (var boneIndex in visual.boneOrder)
			{
				var bone = visual.bones[boneIndex];
				if (!DrawSpineToClavicle && bone.From == visual.spine3 && bone.Arm != Side.None)
					continue;
				var boneDepth = visual.BoneDepth(bone);
				if (visual.Accessory != null && !headAdded && headDepth <= boneDepth)
				{
					visual.AddItemsThrough(drawItems, ref accessoryIndex, headDepth);
					drawItems.Add(new(RigDrawKind.Head, -1, false));
					headAdded = true;
				}
				visual.AddItemsThrough(drawItems, ref accessoryIndex, boneDepth);
				BoneWidths(bone, out var fromWidth, out var toWidth);
				var rings = bone.Part == Part.Hand ? JointRings.None : Rings<Bone>(visual.bones, boneIndex, added);
				added[boneIndex] = true;
				drawItems.Add(new(bone.Part == Part.Hand ? RigDrawKind.Hand : RigDrawKind.Bone, boneIndex,
					bone.Leg == visual.backLeg || bone.Arm == visual.backArm, visual.projectedPose[bone.From], visual.projectedPose[bone.To],
					fromWidth, toWidth, BoneColor(bone, BoneRenderDepth(bone)), rings, bone.Parent >= 0));
				AddCostumeItems(drawItems, costume, bone.Tags);
			}
			if (!headAdded)
			{
				visual.AddItemsThrough(drawItems, ref accessoryIndex, headDepth);
				drawItems.Add(new(RigDrawKind.Head, -1, false));
			}
			visual.AddItemsThrough(drawItems, ref accessoryIndex, float.PositiveInfinity);
		}

		RigSkin Skin(Surface? surface, Texture? texture, float amount, bool normals)
			=> new(visual.SkipSurfaces ? null : surface, texture, amount, normals, NeuralSize, NeuralOrigin, surfaceMaterialOrigin,
				visual.owner.Time.SecondsF, NoSurfaces: visual.SkipSurfaces);

		void DrawItems(Batcher batcher, Color tint, bool silhouette, DrawParts parts)
		{
			if (silhouette)
			{
				DrawSilhouette(batcher, tint, parts);
				return;
			}

			var skin = Skin(visual.Settings.SkinMode == RigSkinMode.Surface ? Assets.GetSurface(visual.Settings.SurfaceId) : null,
				visual.Settings.SkinMode == RigSkinMode.NCA && visual.Neural.NeuralColorAmount > 0 ? neuralSkin.OutputTexture : null,
				visual.Neural.NeuralColorAmount, normals: false);
			skin.Push(batcher);
			var outline = Multiply(visual.Settings.OutlineColor, tint);
			var rimmed = visual.Settings.OutlineMode == RigOutlineMode.Silhouette;
			if (rimmed)
				DrawSilhouette(batcher, outline, parts);
			var frontLimbOutline = Color.Black * visual.Settings.FrontLimbOutline * (tint.A / 255f);
			foreach (var item in drawItems)
			{
				if (!Includes(item, parts))
					continue;
				var pieceOutline = !rimmed ? outline
					: item.Kind is RigDrawKind.Bone or RigDrawKind.Hand && InFrontOfBody(visual.bones[item.Index]) ? frontLimbOutline
					: Color.Transparent;
				switch (item.Kind)
				{
					case RigDrawKind.Costume:
						DrawCostume(batcher, item, neuralBoneUVs[item.Index], Multiply(tint, CostumeShade(visual.bones[item.Index])),
							rimmed ? Color.Transparent : visual.Settings.OutlineColor, skin);
						break;
					case RigDrawKind.Bone:
						DrawBone(batcher, item, neuralBoneUVs[item.Index], Multiply(item.Color, tint), pieceOutline, skin);
						break;
					case RigDrawKind.Hand:
						var color = Multiply(item.Color, tint);
						var spinAmount = HandSpinAmount(visual.bones[item.Index]);
						DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth, spinAmount, 2, pieceOutline);
						DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth, spinAmount, 0, color);
						DrawHandPalmSkin(batcher, item, color, skin);
						break;
					case RigDrawKind.Head:
						visual.DrawHead(batcher, tint, silhouette: false, skin, outlined: !rimmed, (neuralHeadUV, neuralHeadUVRadius));
						break;
					case RigDrawKind.Accessory:
						visual.Accessory!.DrawItem(batcher, item.Index, null);
						break;
				}
			}
			skin.Pop(batcher);
		}

		// Bone and hand outline shapes go down first so the silhouette is their union; the other items follow in order.
		void DrawSilhouette(Batcher batcher, Color tint, DrawParts parts)
		{
			foreach (var item in drawItems)
			{
				if (!Includes(item, parts))
					continue;
				if (item.Kind == RigDrawKind.Costume)
					DrawCostume(batcher, item, default, tint, tint, default, silhouette: true);
				else if (item.Kind == RigDrawKind.Hand)
					DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth,
						HandSpinAmount(visual.bones[item.Index]), 2, tint);
				else if (item.Kind == RigDrawKind.Bone)
					DrawBoneSilhouette(batcher, item, tint);
			}
			foreach (var item in drawItems)
			{
				if (!Includes(item, parts))
					continue;
				switch (item.Kind)
				{
					case RigDrawKind.Head:
						visual.DrawHead(batcher, tint, silhouette: true, default);
						break;
					case RigDrawKind.Accessory:
						visual.Accessory!.DrawItem(batcher, item.Index, tint);
						break;
				}
			}
		}

		bool InFrontOfBody(Bone bone)
			=> (bone.Arm != Side.None || bone.Leg != Side.None) && (bone.Tags & (RigTags.Shoulder | RigTags.Hip)) == 0 &&
				visual.BoneDepth(bone) - visual.jointDepth[visual.pelvis] > visual.Settings.FrontLimbDepth;

		void DrawItemNormals(Batcher batcher, DrawParts parts)
		{
			var flat = new Color(0x8080ff);
			var skin = Skin(visual.Settings.SkinMode == RigSkinMode.Surface &&
				Assets.GetSurface(visual.Settings.SurfaceId) is { HasNormal: true } foundSurface ? foundSurface : null,
				visual.Settings.SkinMode == RigSkinMode.NCA && visual.Neural.NeuralNormalAmount > 0 ? neuralSkin.OutputNormalTexture : null,
				visual.Neural.NeuralNormalAmount, normals: true);
			skin.Push(batcher);
			var phaseSeed = (uint)(visual.animationPhase * uint.MaxValue);
			foreach (var item in drawItems)
			{
				if (!Includes(item, parts))
					continue;
				switch (item.Kind)
				{
					case RigDrawKind.Costume:
						DrawCostume(batcher, item, neuralBoneUVs[item.Index], Color.White, visual.Settings.OutlineColor, skin);
						break;
					case RigDrawKind.Bone:
						DrawBoneNormals(batcher, item, neuralBoneUVs[item.Index], visual.Settings.NormalRoundness,
							visual.Settings.NormalIrregularity, phaseSeed, skin);
						break;
					case RigDrawKind.Hand:
						DrawHand(batcher, item.From, item.To, item.FromWidth, item.ToWidth,
							HandSpinAmount(visual.bones[item.Index]), 2, flat);
						DrawHandPalmSkin(batcher, item, flat, skin);
						break;
					case RigDrawKind.Head:
						visual.DrawHeadNormals(batcher, flat, skin, (neuralHeadUV, neuralHeadUVRadius));
						break;
					case RigDrawKind.Accessory:
						visual.Accessory!.DrawItem(batcher, item.Index, flat);
						break;
				}
			}
			skin.Pop(batcher);
		}

		float HandSpinAmount(Bone bone)
			=> !visual.spinLeftHandOnly || bone.Arm == Side.Left ? Math.Clamp(visual.fingerSpinSpeed / FingerSpinBlendSpeed, 0, 1) : 0;

		void DrawHand(Batcher batcher, Vector2 from, Vector2 to, float fromWidth, float toWidth,
			float spinAmount, float inflate, Color color)
		{
			var along = (to - from).Normalized(Vector2.UnitY);
			var across = along.TurnLeft();
			var width = MathF.Max(fromWidth, toWidth);
			var palmEnd = Vector2.Lerp(from, to, HandPalmRatio);
			var fingerTip = to + along * (width * HandFingerExtension);
			var spin = visual.fingerSpinAngle;
			var fingerRootWidth = width * HandFingerWidth;
			var fingerTipWidth = width * HandFingerTipWidth;
			DrawBoneQuad(batcher, from, palmEnd, fromWidth * 0.72f + inflate, width + inflate, color);
			for (var side = -1; side <= 1; side += 2)
			{
				var rootOffset = across * (width * HandFingerRootSpread * side);
				var tipOffset = fingerTip - palmEnd + across * (width * HandFingerTipSpread * side);
				if (spinAmount > 0)
				{
					rootOffset = Vector2.Lerp(rootOffset, Rotate(rootOffset, spin), spinAmount);
					tipOffset = Vector2.Lerp(tipOffset, Rotate(tipOffset, spin), spinAmount);
				}
				DrawBoneQuad(batcher,
					palmEnd + rootOffset, palmEnd + tipOffset,
					fingerRootWidth + inflate, fingerTipWidth + inflate, color);
			}
		}

		static Vector2 Rotate(Vector2 value, float radians)
		{
			var cos = MathF.Cos(radians);
			var sin = MathF.Sin(radians);
			return new(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
		}

		void DrawHandPalmSkin(Batcher batcher, in RigDrawItem item, Color color, in RigSkin skin)
		{
			var palmEnd = Vector2.Lerp(item.From, item.To, HandPalmRatio);
			var uv = neuralBoneUVs[item.Index];
			var palmUV = new BoneUV(uv.A, uv.B,
				Vector2.Lerp(uv.B, uv.C, HandPalmRatio), Vector2.Lerp(uv.A, uv.D, HandPalmRatio));
			skin.Bone(batcher, item.From, palmEnd, item.FromWidth * 0.72f, MathF.Max(item.FromWidth, item.ToWidth), palmUV, color);
		}

		static Vector2 NeuralUV(Vector2 position) => (position - (Vector2)NeuralOrigin) / (Vector2)NeuralSize;

		public void PerturbNeuralTexture(NeuralTextureActor.PerturbContext context)
		{
			if (visual.Settings.SkinMode != RigSkinMode.NCA || context.Texture != neuralSkin.Texture)
				return;
			var center = (Vector2)visual.owner.Position + (visual.projectedPose[visual.pelvis] + visual.projectedPose[visual.head]) * 0.5f;
			if (visual.neuralHitRemaining > 0)
				context.AddStamp(center, center, 7, visual.NeuralReactions.NeuralHitStrength);
			AddFootContactStamp(context, visual.leftFoot);
			AddFootContactStamp(context, visual.rightFoot);

			var now = visual.owner.Time.SecondsF;
			if (visual.NeuralReactions.NeuralJostleInterval <= 0 || visual.NeuralReactions.NeuralJostleStrength <= 0 ||
				visual.NeuralReactions.NeuralJostleRadius <= 0 || now < neuralJostleAt)
				return;
			neuralJostleAt = now + visual.NeuralReactions.NeuralJostleInterval;
			var phase = now * 2.3f + neuralJostlePhase;
			var offset = new Vector2(MathF.Sin(phase) * 8, MathF.Cos(phase * 0.73f) * 14);
			context.AddStamp(center + offset, center + offset, visual.NeuralReactions.NeuralJostleRadius,
				visual.NeuralReactions.NeuralJostleStrength);
		}

		void AddFootContactStamp(NeuralTextureActor.PerturbContext context, FootIK foot)
		{
			if (foot.ContactRemaining > 0 && visual.NeuralReactions.NeuralFootContactStrength > 0 &&
				visual.NeuralReactions.NeuralFootContactRadius > 0)
				context.AddStamp(foot.ContactPosition, foot.ContactPosition, visual.NeuralReactions.NeuralFootContactRadius,
					visual.NeuralReactions.NeuralFootContactStrength *
					Math.Clamp(foot.ContactRemaining / NeuralFootContactDuration, 0, 1));
		}
	}
}
