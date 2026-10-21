using System.Numerics;
using System.Runtime.InteropServices;
using Foster.Framework;
using static Myth.RigDraw;

namespace Myth;

partial class HumanoidRigVisual
{
	// The body as one ray-marched SDF (SculptedBody): fleshy masses smooth-unioned over metal joints. It fights with
	// what it draws, each piece's screen footprint flagged per piece, and hits dent it where they land.
	sealed class SculptBody(HumanoidRigVisual visual) : IBody
	{
		SculptedBody? sculptedBody;
		readonly List<BodyPrimitive> bodyPrimitives = [];
		bool primitivesDirty = true;
		bool textureDirty = true;

		// A dent stays where it landed on a bone: its surface point and normal in that bone's frame (meters along, across,
		// out), so it rides the limb and keeps its side through a turn. Depth springs back to flush on flesh.
		struct BodyDent
		{
			public int Bone;
			public Vector3 Surface, Normal;
			public float Radius, Depth, Velocity, Age;
			public bool Flesh;
			public Color Color;
		}

		readonly List<BodyDent> dents = [];
		const int MaxDents = 8;
		static float DentRadius => 0.07f; // meters, at strength 1
		static float FleshDentDepth => 0.035f;
		static float MetalDentDepth => 0.025f;
		static float FleshDentSpring => 140f;
		static float FleshDentDamping => 5f;

		public void Posed()
		{
			var span = CollectionsMarshal.AsSpan(dents);
			for (var i = 0; i < span.Length; i++)
				if (span[i].Flesh)
				{
					ref var dent = ref span[i];
					Spring.Step(ref dent.Depth, ref dent.Velocity, 0, FleshDentSpring, FleshDentDamping, visual.visualDelta);
					dent.Age += visual.visualDelta;
				}
			dents.RemoveAll(static dent => dent.Flesh && dent.Age > 1 && MathF.Abs(dent.Depth) < 0.002f);
			primitivesDirty = textureDirty = true;
		}

		// Dents the sculpted body where an attack's world rect touches it.
		public void Dent(in RectInt attack, float strength = 1)
		{
			if (BodyContact((Rect)(attack - visual.owner.Position), CombatRole.Vulnerable, out var contact))
				Dent(visual.owner.Position + contact, strength);
		}

		// Dents the sculpted body at a world point: on the surface under it, or, if it misses the silhouette, the nearest
		// surface toward the torso.
		public void Dent(Vector2 worldPoint, float strength = 1)
		{
			var pieces = CollectionsMarshal.AsSpan(Primitives);
			var local = worldPoint - visual.owner.Position;
			Vector3 hit = default;
			var found = false;
			for (var i = 0; i <= 12 && !found; i++)
				found = SculptedBody.Raycast(pieces, Vector2.Lerp(local, visual.TorsoCenter, i / 12f), out hit);
			if (!found)
				return;
			var normal = SculptedBody.Normal(pieces, hit);
			var piece = SculptedBody.NearestPiece(pieces, hit);
			var bone = 0;
			var closest = float.MaxValue;
			for (var i = 0; i < visual.bones.Length; i++)
			{
				var (from, to) = (visual.ViewJoint(visual.bones[i].From), visual.ViewJoint(visual.bones[i].To));
				var along = to - from;
				var t = Math.Clamp(Vector3.Dot(hit - from, along) / MathF.Max(along.LengthSquared(), 0.0001f), 0, 1);
				var distance = Vector3.DistanceSquared(hit, from + along * t);
				if (distance < closest)
					(bone, closest) = (i, distance);
			}
			var (origin, axis, across, outward) = DentFrame(bone);
			var ppm = visual.Settings.PixelsPerMeter;
			var offset = hit - origin;
			var flesh = piece >= 0 && pieces[piece].Material == 0;
			if (dents.Count == MaxDents)
				dents.RemoveAt(0);
			dents.Add(new()
			{
				Bone = bone,
				Surface = new Vector3(Vector3.Dot(offset, axis), Vector3.Dot(offset, across), Vector3.Dot(offset, outward)) / ppm,
				Normal = new(Vector3.Dot(normal, axis), Vector3.Dot(normal, across), Vector3.Dot(normal, outward)),
				Radius = DentRadius * strength,
				Depth = (flesh ? FleshDentDepth : MetalDentDepth) * strength,
				Flesh = flesh,
				Color = piece >= 0 ? pieces[piece].Color : default,
			});
			primitivesDirty = textureDirty = true;
		}

		// A bone's frame in view pixels. Across and outward flip with the facing mirror, so what's on a limb's front stays
		// on its front when the figure turns.
		(Vector3 Origin, Vector3 Axis, Vector3 Across, Vector3 Outward) DentFrame(int bone)
		{
			var from = visual.ViewJoint(visual.bones[bone].From);
			var axis = Vector3.Normalize(visual.ViewJoint(visual.bones[bone].To) - from);
			if (!float.IsFinite(axis.X))
				axis = Vector3.UnitY;
			var mirror = visual.FacingSide();
			var across = Vector3.Cross(axis, Vector3.UnitZ);
			across = across.LengthSquared() > 0.0001f ? Vector3.Normalize(across) * mirror : Vector3.UnitX;
			return (from, axis, across, Vector3.Cross(across, axis) * mirror);
		}

		// Re-marched once per pose step, however many passes draw it.
		SculptedBody Current()
		{
			sculptedBody ??= new(visual.owner.Game.Manager.GraphicsDevice);
			if (!textureDirty)
				return sculptedBody;
			textureDirty = false;
			var ppm = visual.Settings.PixelsPerMeter;
			var shading = new BodyShading(visual.Settings.Sculpt.BodyShading, visual.Settings.Sculpt.BodyShadow,
				new(visual.Settings.Sculpt.SkinMetallic, visual.Settings.Sculpt.SkinRoughness, visual.Settings.Sculpt.Specular, 0), new(1, visual.Settings.Sculpt.MetalRoughness, visual.Settings.Sculpt.Specular, 0),
				0.05f * ppm, visual.jointDepth[visual.pelvis] * ppm, 0.25f * ppm, visual.Settings.Sculpt.BodyDepthShade);
			Span<BodyLight> lights = stackalloc BodyLight[SculptedBody.MaxLights];
			sculptedBody.Render(CollectionsMarshal.AsSpan(Primitives), shading,
				lights[..visual.GatherLights(lights, visual.owner.RenderPosition + visual.TorsoCenter, ppm)], visual.owner.RenderPosition);
			return sculptedBody;
		}

		// The fleshy robot: soft masses smooth-unioned over a torso, hard metal joints, forearms, shins, hands and feet.
		// Sizes are meters; positions come from the projected pose (so leans, IK and squish carry over) plus its depth,
		// which mirrors rather than rotates on a turn, so the front always faces the camera.
		void BuildBodyPrimitives()
		{
			const int Flesh = 0, Metal = 1;
			var ppm = visual.Settings.PixelsPerMeter;
			var bulk = ppm * visual.Settings.Sculpt.BodyBulk;
			var blend = visual.Settings.Sculpt.FleshBlend * ppm;
			var machined = 0.01f * ppm;
			var muscle = 0.02f * ppm; // a tight blend, so each muscle keeps a crease where it meets the limb
			var list = bodyPrimitives;
			list.Clear();
			const float HeatSwell = 0.22f, HeatBlend = 0.04f, HeatColor = 0.8f;
			var heat = visual.SculptHeat[(int)SculptRegion.Torso];

			var side = Vector3.Normalize(V(visual.rightClavicle) - V(visual.leftClavicle));
			var up = Vector3.Normalize(V(visual.neck) - V(visual.pelvis));
			var forward = Vector3.Normalize(Vector3.Cross(up, side));
			var toes = V(visual.leftLeg.Toe) - V(visual.leftLeg.Ankle) + V(visual.rightLeg.Toe) - V(visual.rightLeg.Ankle);
			if (Vector3.Dot(forward, toes) < 0)
				forward = -forward;

			var (torso, joints) = (visual.Settings.Sculpt.BodyColor, visual.Settings.Sculpt.JointColor);
			var hips = visual.Settings.Sculpt.HipSize;
			Add(V(visual.leftLeg.Hip), V(visual.rightLeg.Hip), 0.1f * hips, 0.1f * hips, 0, joints, Metal);
			Add(V(visual.spine1), V(visual.spine2), 0.13f, 0.14f, blend, torso);
			Add(V(visual.spine2), V(visual.spine3), 0.15f, 0.17f, blend, torso);
			Add(V(visual.leftClavicle) - up * 0.06f * bulk, V(visual.rightClavicle) - up * 0.06f * bulk, 0.12f, 0.12f, blend, torso);
			// Pecs are flat plates from the sternum up to the armpit, not balls: they barely stand off the ribcage. A shallow
			// groove along each one's underside gives it the crisp lower edge.
			var chest = Vector3.Lerp(V(visual.spine2), V(visual.spine3), 0.8f);
			var pecs = visual.Settings.Sculpt.ChestMass;
			if (pecs > 0)
			{
				for (var s = -1; s <= 1; s += 2)
				{
					var (inner, outer) = Pec(s);
					Add(inner, outer, 0.06f * pecs, 0.05f * pecs, muscle, torso);
				}
				for (var s = -1; s <= 1; s += 2)
				{
					var (inner, outer) = Pec(s);
					var under = forward * 0.02f * bulk;
					list.Add(new(inner - up * 0.055f * bulk * pecs + under, 0.012f * bulk,
						outer - up * 0.045f * bulk * pecs + under, 0.012f * bulk, 0.01f * ppm, default, Carve: true));
				}
			}
			if (visual.Settings.Sculpt.Abs > 0)
				for (var row = 0; row < 3; row++)
				{
					var center = Vector3.Lerp(V(visual.pelvis), V(visual.spine2), 0.35f + row * 0.25f) + forward * 0.12f * bulk;
					for (var s = -1; s <= 1; s += 2)
					{
						var ab = center + side * s * 0.045f * bulk;
						Add(ab, ab, 0.04f * visual.Settings.Sculpt.Abs, 0.04f * visual.Settings.Sculpt.Abs, 0.015f * ppm, torso);
					}
				}
			Add(V(visual.neck), V(visual.head), 0.075f, 0.07f, blend, torso);
			heat = 0;
			var (arms, forearms, hands) = (torso, visual.Settings.Sculpt.TrimColor, joints);
			var (legs, shins, feet) = (visual.Settings.Sculpt.LegsColor, visual.Settings.Sculpt.TrimColor, joints);
			heat = Heat(SculptRegion.LeftArm);
			Limb(visual.leftUpperArm, visual.leftLowerArm, visual.leftHand, visual.leftPalm, 0.1f, 0.06f, 0.05f, 0.06f, 0.05f, arms, forearms, hands);
			heat = Heat(SculptRegion.RightArm);
			Limb(visual.rightUpperArm, visual.rightLowerArm, visual.rightHand, visual.rightPalm, 0.1f, 0.06f, 0.05f, 0.06f, 0.05f, arms, forearms, hands);
			heat = Heat(SculptRegion.LeftLeg);
			Limb(visual.leftLeg.Hip, visual.leftLeg.Knee, visual.leftLeg.Ankle, visual.leftLeg.Toe, 0.13f * hips, 0.09f * hips, 0.07f, 0.075f, 0.055f, legs, shins, null);
			Boot(visual.leftLeg);
			heat = Heat(SculptRegion.RightLeg);
			Limb(visual.rightLeg.Hip, visual.rightLeg.Knee, visual.rightLeg.Ankle, visual.rightLeg.Toe, 0.13f * hips, 0.09f * hips, 0.07f, 0.075f, 0.055f, legs, shins, null);
			Boot(visual.rightLeg);
			heat = 0;
			// The codpiece: a rounded mass below the hip joints, pushed forward and smoothed into the hips and thighs.
			if (visual.Settings.Sculpt.Codpiece > 0)
			{
				var crotch = Vector3.Lerp(V(visual.leftLeg.Hip), V(visual.rightLeg.Hip), 0.5f) + (forward * 0.07f - up * 0.06f) * bulk;
				Add(crotch + up * 0.03f * bulk, crotch + (forward * 0.02f - up * 0.04f) * bulk,
					0.065f * visual.Settings.Sculpt.Codpiece, 0.055f * visual.Settings.Sculpt.Codpiece, blend, legs);
			}
			for (var s = -1; s <= 1; s += 2)
			{
				var (upperArm, lowerArm) = s < 0 ? (visual.leftUpperArm, visual.leftLowerArm) : (visual.rightUpperArm, visual.rightLowerArm);
				heat = Heat(s < 0 ? SculptRegion.LeftArm : SculptRegion.RightArm);
				Muscle(V(upperArm), V(lowerArm), 0.3f, 0.7f, forward, 0.03f, 0.06f, 0.05f, arms); // biceps
				Muscle(V(upperArm), V(lowerArm), 0.15f, 0.6f, -forward, 0.03f, 0.06f, 0.045f, arms); // triceps
				var leg = s < 0 ? visual.leftLeg : visual.rightLeg;
				heat = Heat(s < 0 ? SculptRegion.LeftLeg : SculptRegion.RightLeg);
				var (hip, knee, ankle) = (V(leg.Hip), V(leg.Knee), V(leg.Ankle));
				var front = Vector3.Normalize(V(leg.Toe) - ankle);
				Muscle(hip, knee, 0.25f, 0.8f, front, 0.04f, 0.085f * hips, 0.065f, legs); // quads
				Muscle(hip, knee, 0.72f, 0.9f, front * 0.5f - side * s, 0.05f, 0.045f, 0.04f, legs); // the teardrop above the knee
				Muscle(hip, knee, 0.2f, 0.75f, -front, 0.04f, 0.08f * hips, 0.06f, legs); // hamstrings
				// Each glute's inner edge rides just past the midline, so the two meet in a crease at any size.
				if (visual.Settings.Sculpt.Glutes > 0)
				{
					var radius = 0.085f * visual.Settings.Sculpt.Glutes;
					var midline = Vector3.Lerp(V(visual.leftLeg.Hip), V(visual.rightLeg.Hip), 0.5f);
					var glute = midline + (side * s * radius * 0.85f - forward * 0.07f - up * 0.03f) * bulk;
					Add(glute + up * 0.02f * bulk, glute - (up * 0.06f + forward * 0.01f) * bulk, radius, radius * 0.88f, muscle, legs);
				}
				Muscle(knee, ankle, 0.15f, 0.55f, -front, 0.05f, 0.07f, 0.045f, legs); // calf, fleshed over the metal shin
			}
			// Claws ride the finger joints, so they curl with the animation's grip, and run on past the fingertips.
			if (visual.Settings.Sculpt.Claws > 0)
				foreach (var claws in (ReadOnlySpan<int[]>)[visual.leftClaws, visual.rightClaws])
					for (var i = 0; i < claws.Length; i += 3)
					{
						heat = Heat(claws == visual.leftClaws ? SculptRegion.LeftArm : SculptRegion.RightArm);
						var (knuckle, middle, tip) = (V(claws[i]), V(claws[i + 1]), V(claws[i + 2]));
						Add(knuckle, middle, 0.022f, 0.016f, machined, visual.Settings.Sculpt.BladeColor, Metal);
						Add(middle, middle + (tip - middle) * (1 + visual.Settings.Sculpt.Claws * 1.5f), 0.016f, 0.003f, machined, visual.Settings.Sculpt.BladeColor, Metal);
					}
			heat = Heat(SculptRegion.Torso);
			for (var s = -1; s <= 1; s += 2)
			{
				var clavicle = s < 0 ? visual.leftClavicle : visual.rightClavicle;
				Add(V(visual.neck), V(clavicle), 0.08f, 0.07f, blend, torso);
			}
			if (visual.Settings.Sculpt.BladeOnlyHarms)
				for (var i = 0; i < list.Count; i++)
					list[i] = list[i] with { Combat = BodyCombat.Vulnerable };
			// A sword in the right fist, the blade running out past the index finger along the grip, a crossguard across it
			// and the pommel out the other side. Only the blade hurts, and nothing of it takes hits.
			if (visual.Settings.Sculpt.Sword > 0)
			{
				heat = Heat(SculptRegion.RightArm);
				var claws = visual.rightClaws;
				var grip = (V(claws[0]) + V(claws[6])) * 0.5f;
				var along = Vector3.Normalize(V(claws[0]) - V(claws[6]));
				if (!float.IsFinite(along.X))
					along = -up;
				var across = Vector3.Normalize(Vector3.Cross(along, Vector3.UnitZ));
				if (!float.IsFinite(across.X))
					across = Vector3.UnitX;
				var (hilt, guard) = (visual.Settings.Sculpt.GripColor, grip + along * 0.06f * ppm);
				var thin = 0.6f / bulk; // meters that draw as 0.6px, so a small figure's sword still shows
				Add(grip - along * 0.07f * ppm, guard, MathF.Max(0.016f, thin), MathF.Max(0.016f, thin), 0, hilt, Metal);
				Add(guard - across * 0.05f * ppm, guard + across * 0.05f * ppm, MathF.Max(0.013f, thin), MathF.Max(0.013f, thin), 0, hilt, Metal);
				Add(guard, guard + along * visual.Settings.Sculpt.Sword * ppm, MathF.Max(0.018f, thin), MathF.Max(0.004f, thin * 0.5f), 0,
					visual.Settings.Sculpt.BladeColor, Metal);
				list[^3] = list[^3] with { Combat = BodyCombat.None };
				list[^2] = list[^2] with { Combat = BodyCombat.None };
				list[^1] = list[^1] with { Combat = BodyCombat.Harmful };
			}
			// Dents carve in last; a flesh dent springing back past flush bulges out for a moment instead.
			foreach (var dent in dents)
			{
				var (origin, axis, across, outward) = DentFrame(dent.Bone);
				var surface = origin + (axis * dent.Surface.X + across * dent.Surface.Y + outward * dent.Surface.Z) * ppm;
				var normal = axis * dent.Normal.X + across * dent.Normal.Y + outward * dent.Normal.Z;
				var (radius, depth) = (dent.Radius * ppm, dent.Depth * ppm);
				var center = depth >= 0 ? surface + normal * (radius - depth) : surface - normal * (radius + depth);
				list.Add(new(center, radius, center, radius, (dent.Flesh ? 0.02f : 0.004f) * ppm, dent.Color, dent.Flesh ? Flesh : Metal,
					Carve: depth >= 0));
			}

			// A fleshy upper segment with its mass at the root, a metal joint, a machined lower segment and end.
			void Limb(int root, int joint, int end, int tip, float mass, float upper, float lower, float jointRadius, float endRadius,
				Color upperColor, Color lowerColor, Color? endColor)
			{
				Add(V(root), V(root), mass, mass, blend, upperColor);
				Add(V(root), V(joint), upper, lower, blend, upperColor);
				Add(V(joint), V(joint), jointRadius, jointRadius, 0, joints, Metal);
				Add(V(joint), V(end), jointRadius, endRadius, machined, lowerColor, Metal);
				if (endColor is { } color)
					Add(V(end), V(tip), endRadius, endRadius * 1.1f, machined, color, Metal);
			}

			// A flat boot in the foot's own frame: the ball and toe tip lie along the sole, the heel sits on that line under
			// the ankle (pushed back by HeelLength), and a shaft runs from partway up the shin down into the heel. Each
			// piece rests its underside on the floor, a sole's thickness below the line, so it rolls and lifts with the foot.
			void Boot(Leg leg)
			{
				var (knee, ankle, ball, tip) = (V(leg.Knee), V(leg.Ankle), V(leg.Toe), V(leg.Tip));
				var sole = Vector3.Normalize(tip - ball);
				if (!float.IsFinite(sole.X))
					sole = forward;
				var under = ball + sole * Vector3.Dot(ankle - ball, sole);
				var down = Vector3.Normalize(under - ankle);
				if (!float.IsFinite(down.X))
					down = Vector3.UnitY;
				var size = visual.Settings.Sculpt.BootSize;
				var floor = 0.015f * ppm;
				Vector3 OnFloor(Vector3 point, float radius) => point - down * (radius * size * bulk - floor);
				var heel = OnFloor(under - sole * visual.Settings.Sculpt.HeelLength * ppm, 0.055f);
				var ballCenter = OnFloor(ball, 0.05f);
				Add(Vector3.Lerp(knee, ankle, 1 - visual.Settings.Sculpt.BootHeight), heel, 0.06f * size, 0.055f * size, 0.02f * ppm, feet, Metal);
				Add(heel, ballCenter, 0.055f * size, 0.05f * size, machined, feet, Metal);
				Add(ballCenter, OnFloor(tip, 0.035f), 0.05f * size, 0.035f * size, machined, feet, Metal);
			}

			(Vector3 Inner, Vector3 Outer) Pec(int s) => (chest + (side * s * 0.045f + forward * 0.12f) * bulk,
				Vector3.Lerp(chest, V(s < 0 ? visual.leftUpperArm : visual.rightUpperArm), 0.65f) + forward * 0.08f * bulk);

			// A mass along part of a segment, pushed off its axis toward a side of the limb.
			void Muscle(Vector3 a, Vector3 b, float from, float to, Vector3 toward, float offset, float radiusA, float radiusB, Color color)
			{
				var axis = Vector3.Normalize(b - a);
				var push = toward - axis * Vector3.Dot(toward, axis);
				push = push.LengthSquared() > 0.000001f ? Vector3.Normalize(push) * offset * bulk : Vector3.Zero;
				Add(Vector3.Lerp(a, b, from) + push, Vector3.Lerp(a, b, to) + push, radiusA, radiusB, muscle, color);
			}

			// Heat swells a piece, softens its blend, and runs its color toward the heat color.
			void Add(Vector3 a, Vector3 b, float radiusA, float radiusB, float k, Color color, int material = Flesh)
			{
				var swell = bulk * (1 + heat * HeatSwell);
				list.Add(new(a, radiusA * swell, b, radiusB * swell, k + heat * HeatBlend * ppm,
					heat > 0 ? Color.Lerp(color, visual.SculptHeatColor, heat * HeatColor) : color, material));
			}

			float Heat(SculptRegion region) => Math.Clamp(visual.SculptHeat[(int)region], 0, 1);

			Vector3 V(int joint) => new(visual.projectedPose[joint], visual.jointDepth[joint] * ppm);
		}

		// Whether rig-local bounds touch a piece that plays `role`; contact is the point of the body nearest the bounds'
		// center (the center itself when it's inside), where a hit lands.
		bool BodyContact(in Rect bounds, CombatRole role, out Vector2 contact)
		{
			var need = role == CombatRole.Harmful ? BodyCombat.Harmful : BodyCombat.Vulnerable;
			var center = bounds.Center;
			var (touched, nearest) = (false, float.MaxValue);
			contact = center;
			foreach (var piece in Primitives)
			{
				if (piece.Carve || (piece.Combat & need) == 0)
					continue;
				var (a, b) = (new Vector2(piece.A.X, piece.A.Y), new Vector2(piece.B.X, piece.B.Y));
				if (!FootprintOverlaps(bounds, a, b, piece.RadiusA, piece.RadiusB))
					continue;
				touched = true;
				var along = b - a;
				var t = Math.Clamp(Vector2.Dot(center - a, along) / MathF.Max(along.LengthSquared(), 0.0001f), 0, 1);
				var axis = a + along * t;
				var radius = float.Lerp(piece.RadiusA, piece.RadiusB, t);
				var outward = center - axis;
				var distance = outward.Length();
				if (distance - radius < nearest)
				{
					nearest = distance - radius;
					contact = distance <= radius ? center : axis + outward / distance * radius;
				}
			}
			return touched;
		}

		// A round cone seen from the front is a tapered capsule; the gap to a rect along it is convex in t.
		static bool FootprintOverlaps(in Rect bounds, Vector2 a, Vector2 b, float radiusA, float radiusB)
		{
			var (center, half) = (bounds.Center, bounds.Size * 0.5f);
			float Gap(float t) => Vector2.Max(Vector2.Abs(Vector2.Lerp(a, b, t) - center) - half, Vector2.Zero).Length() -
				float.Lerp(radiusA, radiusB, t);
			var (low, high) = (0f, 1f);
			for (var i = 0; i < 12; i++)
			{
				var (left, right) = (float.Lerp(low, high, 1 / 3f), float.Lerp(low, high, 2 / 3f));
				if (Gap(left) < Gap(right))
					high = right;
				else
					low = left;
			}
			return Gap((low + high) * 0.5f) <= 0;
		}
		// The head shades from its own color like the body's parts.
		public (Color Near, Color Core, Color Far) HeadPalette
		{
			get
			{
				var near = visual.Settings.Sculpt.SkinColor;
				var far = new Color((Vector4)near * (Vector4)visual.Settings.Sculpt.BodyShadow);
				return (near, Color.Lerp(near, far, 0.5f), far);
			}
		}

		// Rebuilt once per pose step, by whichever of drawing, combat or a dent asks first.
		List<BodyPrimitive> Primitives
		{
			get
			{
				if (primitivesDirty)
				{
					primitivesDirty = false;
					BuildBodyPrimitives();
				}
				return bodyPrimitives;
			}
		}

		// One piece, in front of the player when straddling: body, head, then accessories.
		public void Draw(Batcher batcher, Color tint, bool silhouette, DrawParts parts)
		{
			if (parts == DrawParts.BehindPlayer)
				return;
			var body = Current();
			if (body.ColorTexture is { } color)
			{
				batcher.Image(body.MaskTexture!, body.Origin, silhouette ? tint : Multiply(visual.Settings.OutlineColor, tint));
				if (!silhouette)
					batcher.Image(color, body.Origin, tint);
			}
			visual.DrawHead(batcher, tint, silhouette, default);
			visual.DrawAccessories(batcher, silhouette ? tint : null);
		}

		public void DrawNormals(Batcher batcher, DrawParts parts)
		{
			if (parts == DrawParts.BehindPlayer)
				return;
			var flat = new Color(0x8080ff);
			if (Current() is { NormalTexture: { } normals } body)
				batcher.Image(normals, body.Origin, Color.White);
			visual.DrawHeadNormals(batcher, flat, default);
			visual.DrawAccessories(batcher, flat);
		}

		public void DrawEmissive(Batcher batcher)
		{
			if (Current() is { SpecularTexture: { } specular } body)
				batcher.Image(specular, body.Origin, Color.White);
		}

		public bool Overlaps(in Rect bounds, CombatRole role) => BodyContact(bounds, role, out _);

		public void DrawCombatBounds(Batcher batcher, Color color)
		{
			foreach (var piece in Primitives)
			{
				if (piece.Carve || piece.Combat == BodyCombat.None)
					continue;
				var tint = piece.Combat == BodyCombat.Harmful ? new Color(0xe64a39) : piece.Combat == BodyCombat.Vulnerable ? new Color(0xe6d739) : color;
				var from = visual.owner.Position + new Vector2(piece.A.X, piece.A.Y);
				var to = visual.owner.Position + new Vector2(piece.B.X, piece.B.Y);
				batcher.CircleLine(from, piece.RadiusA, 1, 12, tint);
				batcher.CircleLine(to, piece.RadiusB, 1, 12, tint);
				batcher.Line(from, to, 1, tint * 0.5f);
			}
		}

		public Rect Bounds(Rect bounds) => Current() is { ColorTexture: { } texture } body
			? bounds.Conflate(new Rect(body.Origin, new Vector2(texture.Width, texture.Height)))
			: bounds;

		public void Dispose() => sculptedBody?.Dispose();
	}
}
