using System.Numerics;
using Foster.Framework;

namespace Kit;

public sealed class PackedNamedSubtexture : PackedSubtexture
{
	public string Name { get; set; } = "";

	public static PackedNamedSubtexture From(string name, PackedSubtexture subtexture)
		=> new() { Name = name, Source = subtexture.Source, Frame = subtexture.Frame };
}

public class PackedSubtexture
{
	public PackedRect Source { get; set; } = new();
	public PackedRect Frame { get; set; } = new();

	public static PackedSubtexture From(Rect source, Rect frame)
		=> new() { Source = PackedRect.From(source), Frame = PackedRect.From(frame) };

	public static PackedSubtexture VisibleFrom(PackedSubtexture subtexture)
		=> new()
		{
			Source = subtexture.Source,
			Frame = new() { X = 0, Y = 0, W = subtexture.Source.W, H = subtexture.Source.H }
		};

	public Subtexture ToSubtexture(Texture atlas)
		=> Source.W > 0 && Source.H > 0 ? new(atlas, Source.ToRect(), Frame.ToRect()) : new();
}

public sealed class PackedSprite
{
	public string Name { get; set; } = "";
	public PackedVector Origin { get; set; } = new();
	public List<PackedSpriteFrame> Frames { get; set; } = [];
	public List<PackedSpriteAnimation> Animations { get; set; } = [];
	public Dictionary<string, RectInt> Slices { get; set; } = [];
	public Dictionary<string, List<PackedSocketPose>> Sockets { get; set; } = [];
}

public sealed class PackedSocketPose
{
	public PackedVector Position { get; set; } = new();
	public PackedVector Normal { get; set; } = new();
}

public sealed class PackedSpriteFrame
{
	public PackedSubtexture Subtexture { get; set; } = new();
	public PackedSubtexture VisibleSubtexture { get; set; } = new();
	public PackedSubtexture NormalSubtexture { get; set; } = new();
	public PackedSubtexture EmissiveSubtexture { get; set; } = new();
	public float Duration { get; set; }
	public string? UserData { get; set; }
}

public sealed class PackedSpriteAnimation
{
	public string Name { get; set; } = "";
	public int FrameStart { get; set; }
	public int FrameCount { get; set; }
	public float Duration { get; set; }
}

public readonly record struct GeneratedSpriteTransition(string FromTag, string ToTag, int FrameCount, float[] Durations);

public static class AsepriteSpriteBaker
{
	public const string DefaultAnimationName = "idle";

	public static PackedSprite BuildPackedSprite(
		string name,
		Aseprite ase,
		Func<string, PackedSubtexture> subtexture,
		Func<string, PackedSubtexture> subtextureOrDefault,
		IReadOnlyDictionary<string, float[]> expandedFrameDurations,
		IReadOnlyDictionary<string, int[]> expandedFrameMap,
		IReadOnlyDictionary<string, List<GeneratedSpriteTransition>> generatedTransitions,
		Func<string, Aseprite, Dictionary<string, Sprite.Socket[]>> buildSocketPoses)
	{
		var origin = Vector2.Zero;
		foreach (var slice in ase.Slices)
			if (slice.Keys.FirstOrDefault() is { Pivot: { } pivot } key)
			{
				origin = key.Bounds.Position + pivot;
				break;
			}

		var packed = new PackedSprite
		{
			Name = name,
			Origin = PackedVector.From(origin)
		};

		foreach (var slice in ase.Slices)
			if (slice.Keys.Length > 0)
				packed.Slices[slice.Name] = slice.Keys[0].Bounds;

		var useExpanded = expandedFrameDurations.TryGetValue(name, out var expDurations);
		var frameCount = useExpanded ? expDurations!.Length : ase.Frames.Length;

		var frameUserData = new string?[frameCount];
		if (useExpanded && expandedFrameMap.TryGetValue(name, out var expMap))
		{
			for (var src = 0; src < expMap.Length && src < ase.Frames.Length; src++)
				frameUserData[expMap[src]] = CelUserData(ase.Frames[src]);
		}
		else
		{
			for (var i = 0; i < frameCount && i < ase.Frames.Length; i++)
				frameUserData[i] = CelUserData(ase.Frames[i]);
		}

		for (var i = 0; i < frameCount; i++)
			packed.Frames.Add(new()
			{
				Subtexture = subtexture($"{name}/{i}"),
				VisibleSubtexture = PackedSubtexture.VisibleFrom(subtexture($"{name}/{i}")),
				NormalSubtexture = subtexture($"{name}/normal/{i}"),
				EmissiveSubtexture = subtextureOrDefault($"{name}/emissive/{i}"),
				Duration = useExpanded ? expDurations![i] : ase.Frames[i].Duration / 1000.0f,
				UserData = frameUserData[i]
			});

		foreach (var tag in ase.Tags)
		{
			if (string.IsNullOrEmpty(tag.Name))
				continue;

			int animFrom, animCount;
			if (useExpanded && expandedFrameMap.TryGetValue(name, out var map))
			{
				animFrom = map[tag.From];
				var animEnd = tag.To + 1 < map.Length ? map[tag.To + 1] : frameCount;
				animCount = animEnd - animFrom;
			}
			else
			{
				animFrom = tag.From;
				animCount = tag.To - tag.From + 1;
			}

			var totalAnimDuration = 0.0f;
			for (var i = animFrom; i < animFrom + animCount && i < packed.Frames.Count; i++)
				totalAnimDuration += packed.Frames[i].Duration;
			packed.Animations.Add(new() { Name = tag.Name, FrameStart = animFrom, FrameCount = animCount, Duration = totalAnimDuration });
		}

		if (ase.Tags.Length == 0 && ase.Frames.Length > 0)
		{
			float totalAnimDuration = 0;
			for (var i = 0; i < ase.Frames.Length; i++)
				totalAnimDuration += packed.Frames[i].Duration;

			packed.Animations.Add(new()
			{
				Name = DefaultAnimationName,
				FrameStart = 0,
				FrameCount = ase.Frames.Length,
				Duration = totalAnimDuration,
			});
		}

		if (generatedTransitions.TryGetValue(name, out var transitions))
		{
			var transFrameStart = packed.Frames.Count;
			foreach (var trans in transitions)
			{
				float duration = 0;
				for (var i = 0; i < trans.FrameCount; i++)
				{
					var frame = subtexture($"{name}/transition/{trans.FromTag}->{trans.ToTag}/{i}");
					packed.Frames.Add(new()
					{
						Subtexture = frame,
						VisibleSubtexture = PackedSubtexture.VisibleFrom(frame),
						NormalSubtexture = subtexture($"{name}/transition/{trans.FromTag}->{trans.ToTag}/normal/{i}"),
						EmissiveSubtexture = subtextureOrDefault($"{name}/transition/{trans.FromTag}->{trans.ToTag}/emissive/{i}"),
						Duration = trans.Durations[i]
					});
					duration += trans.Durations[i];
				}
				packed.Animations.Add(new() { Name = $"{trans.FromTag}->{trans.ToTag}", FrameStart = transFrameStart, FrameCount = trans.FrameCount, Duration = duration });
				transFrameStart += trans.FrameCount;
			}
		}

		foreach (var (socketName, poses) in buildSocketPoses(name, ase))
		{
			if (poses.Length != packed.Frames.Count)
				throw new InvalidOperationException($"Sprite '{name}' socket '{socketName}' has {poses.Length} poses for {packed.Frames.Count} frames");
			var packedPoses = new List<PackedSocketPose>(poses.Length);
			foreach (var pose in poses)
				packedPoses.Add(new() { Position = PackedVector.From(pose.Position), Normal = PackedVector.From(pose.Normal) });
			packed.Sockets[socketName] = packedPoses;
		}

		return packed;
	}

	static string? CelUserData(Aseprite.Frame frame)
	{
		foreach (var cel in frame.Cels)
			if (!string.IsNullOrEmpty(cel.UserData.Text))
				return cel.UserData.Text;
		return null;
	}
}

public sealed class PackedRect
{
	public float X { get; set; }
	public float Y { get; set; }
	public float W { get; set; }
	public float H { get; set; }

	public static PackedRect From(Rect rect)
		=> new() { X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height };

	public Rect ToRect()
		=> new(X, Y, W, H);
}

public sealed class PackedVector
{
	public float X { get; set; }
	public float Y { get; set; }

	public static PackedVector From(Vector2 vector)
		=> new() { X = vector.X, Y = vector.Y };

	public Vector2 ToVector2()
		=> new(X, Y);
}

public static class PackedSpriteLoader
{
	public static void Load(Texture atlas, IEnumerable<PackedSprite> packedSprites, Dictionary<string, Sprite> sprites)
	{
		var foundSprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var packed in packedSprites)
		{
			foundSprites.Add(packed.Name);
			if (!sprites.TryGetValue(packed.Name, out var sprite))
			{
				sprite = new Sprite(packed.Name, packed.Origin.ToVector2());
				sprites.Add(packed.Name, sprite);
			}

			var frames = new List<Sprite.Frame>(packed.Frames.Count);
			foreach (var frame in packed.Frames)
				frames.Add(new(
					frame.Subtexture.ToSubtexture(atlas),
					frame.VisibleSubtexture.ToSubtexture(atlas),
					frame.NormalSubtexture.ToSubtexture(atlas),
					frame.EmissiveSubtexture.ToSubtexture(atlas),
					frame.Duration,
					frame.UserData));

			var animations = new List<Sprite.Animation>(packed.Animations.Count);
			foreach (var animation in packed.Animations)
				animations.Add(new(animation.Name, animation.FrameStart, animation.FrameCount, animation.Duration));

			var sockets = new Dictionary<string, Sprite.Socket[]>(StringComparer.OrdinalIgnoreCase);
			foreach (var (socketName, packedPoses) in packed.Sockets)
			{
				var poses = new Sprite.Socket[packedPoses.Count];
				for (var i = 0; i < poses.Length; i++)
					poses[i] = new(packedPoses[i].Position.ToVector2(), packedPoses[i].Normal.ToVector2());
				sockets[socketName] = poses;
			}

			sprite.Reload(packed.Origin.ToVector2(), frames, animations, packed.Slices, sockets);
		}

		foreach (var (name, sprite) in sprites.ToArray())
		{
			if (foundSprites.Contains(name))
				continue;
			sprite.Reload(Vector2.Zero, [], []);
			sprites.Remove(name);
		}
	}
}
