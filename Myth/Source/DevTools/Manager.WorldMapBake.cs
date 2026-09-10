#if EDITOR
using System.Numerics;
using Foster.Framework;

namespace Myth;

// Bakes every room into the single world map image the map overlay samples (see WorldMap.cs).
// Each room renders through the same path the editor's zoomed-out room previews use --
// a throwaway Game plus Game.RenderWorldToTarget -- into a target sized to the room's footprint
// in the map, which is then blitted into one big target and read back to disk.
//
// This needs a real graphics device, so it runs from the editor rather than from the headless
// --build-asset-pack; the files land in the pack folder and a pack rebuild leaves them alone.
public partial class Manager
{
	string WorldMapBakeDirectory => assetPackPath ?? Assets.DefaultVisualAssetPackPath;

	public void BakeWorldMap()
	{
		var bounds = WorldMap.WorldPixelBounds();
		var (size, scale) = WorldMap.BakeSize(bounds);
		if (size.X <= 0 || size.Y <= 0)
		{
			Console.WriteLine("World map bake: no rooms to bake.");
			return;
		}

		var timer = System.Diagnostics.Stopwatch.StartNew();
		using var target = new Target(GraphicsDevice, size.X, size.Y, name: "WorldMapBake");
		target.Clear(Color.Transparent);

		var baked = 0;
		foreach (var room in Assets.RoomsById.Values)
		{
			if (room.Sublayer != 0)
				continue;
			if (BakeWorldMapRoom(room, bounds, target) )
				baked++;
		}

		WriteWorldMapBake(target, bounds);
		Console.WriteLine($"World map bake ({Assets.CurrentWorldId}): {baked} rooms -> {size.X}x{size.Y} ({scale:0.###} px per world px) in {timer.ElapsedMilliseconds}ms");

		// Pick the fresh files up immediately so the map shows the new bake without a restart.
		Assets.ReloadWorldMap(GraphicsDevice);
	}

	bool BakeWorldMapRoom(Room room, RectInt bounds, Target target)
	{
		// Room edges are placed by rounding both sides into map space, so rooms that share a
		// world edge share a map edge too and the bake has no seams between them.
		var destination = WorldMapDestination(room.WorldBounds, bounds, target);
		if (destination.Width <= 0 || destination.Height <= 0)
			return false;

		// Rendered at the room's own resolution and scaled down on the way in: rendering
		// straight into the (rounded) destination size would letterbox, since the final
		// composite fits the view uniformly and would leave a sliver of the target unwritten.
		var world = room.WorldBounds;
		using var roomTarget = new Target(GraphicsDevice, world.Width, world.Height, name: $"WorldMapBake/{room.Id}");
		roomTarget.Clear(Color.Transparent);
		using (var roomGame = new Game(this, room, audioEventsEnabled: false, spawnBud: false, enableParticles: false, loadGoals: false))
			roomGame.RenderWorldToTarget(roomTarget, new Rect(world.X, world.Y, world.Width, world.Height));

		batcher.PushSampler(new(TextureFilter.Linear, TextureWrap.Clamp, TextureWrap.Clamp));
		batcher.ImageStretch(new Subtexture(roomTarget.Attachments[0]), destination, Color.White);
		batcher.PopSampler();
		batcher.Render(target);
		batcher.Clear();
		return true;
	}

	static RectInt WorldMapDestination(RectInt worldRect, RectInt bounds, Target target)
	{
		var scaleX = target.Width / (float)bounds.Width;
		var scaleY = target.Height / (float)bounds.Height;
		var left = (int)MathF.Round((worldRect.Left - bounds.X) * scaleX);
		var top = (int)MathF.Round((worldRect.Top - bounds.Y) * scaleY);
		var right = (int)MathF.Round((worldRect.Right - bounds.X) * scaleX);
		var bottom = (int)MathF.Round((worldRect.Bottom - bounds.Y) * scaleY);
		return new RectInt(left, top, right - left, bottom - top);
	}

	void WriteWorldMapBake(Target target, RectInt bounds)
	{
		var pixels = new Color[target.Width * target.Height];
		target.Attachments[0].GetData<Color>(pixels);

		Directory.CreateDirectory(WorldMapBakeDirectory);
		// A manual editor bake can replace a different world's cached release bake.
		File.Delete(Path.Join(WorldMapBakeDirectory, "world-map.inputs.sha256"));
		using (var image = new Image(target.Width, target.Height, pixels))
			image.WriteQoi(Path.Join(WorldMapBakeDirectory, WorldMap.ImageFile));
		File.WriteAllBytes(Path.Join(WorldMapBakeDirectory, WorldMap.BoundsFile), WorldMap.WriteBounds(bounds));
	}
}
#endif
