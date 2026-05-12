using System.Numerics;
using Foster.Framework;

namespace TinyLink;

public class Game
{
	public const int Width = 240;
	public const int Height = 135;
	public const int Columns = Width / TileSize;
	public const int Rows = Height / TileSize + 1;
	public const int TileSize = 8;
	public const float AudioPixelsPerUnit = 32;

	public readonly Manager Manager;
	public readonly Controls Controls;
	public readonly Batcher Batcher;
	public readonly Target Screen;
	public readonly List<Actor> Actors = [];

	public Input Input => Manager.Input;
	public GraphicsDevice GraphicsDevice => Manager.GraphicsDevice;
	public Audio Audio => Manager.Audio;
	public Time Time => Manager.Time;

	private Room? room;
	private Room? nextRoom;
	private float nextRoomEase;
	private readonly List<Actor> destroying = [];
	private readonly List<Actor> rendering = [];
	private readonly HashSet<string> audioFailures = [];
	private readonly Material? crtMaterial;
	private SoundInstance? music;
	private string? musicArea;
	private float hitStun = 0;
	private float shaking = 0;
	private Point2 shake;
	private Rng rng = new();
	private bool showCollisionRects;

	public Vector2 Camera { get; private set; }
	public RectInt Bounds => room?.WorldBounds ?? new();
	public Point2 Cell => room?.Cell ?? Point2.Zero;
	public Room? CurrentRoom => room;

	public Game(Manager manager, Point2 start)
	{
		Manager = manager;
		Batcher = new(manager.GraphicsDevice, name: "GameBatcher");
		Screen = new(manager.GraphicsDevice, Width, Height, name: "GameScreen");
		Controls = new(manager.Input);
		crtMaterial = CreateCrtMaterial();

		if (Assets.Rooms.TryGetValue(start, out room))
		{
			Camera = new Vector2(room.Cell.X * Width, room.Cell.Y * Height);
			UpdateAudioListener();
			LoadRoom(room);
			StartMusic();
			ApplyMusicArea(room);
		}
	}

	public void Update()
	{
		// Don't run normal gameplay loop if no room is loaded
		if (room == null)
			return;

		// Run Game normally when not moving to a new room
		if (nextRoom == null)
		{
			// Reload Room Debug
			if (Input.Keyboard.Pressed(Keys.R))
				ReloadRoom();

			if (Input.Keyboard.Pressed(Keys.C))
				showCollisionRects = !showCollisionRects;

			// only run normal updates if no hitStun
			if (hitStun <= 0)
			{
				// Remove Destroyed Actors
				for (int i = 0; i < destroying.Count; i ++)
				{
					destroying[i].Destroyed();
					Actors.Remove(destroying[i]);
				}
				destroying.Clear();

				// Update Actors
				for (var i = 0; i < Actors.Count; i ++)
					Actors[i].Update();

				// screen shaking
				if (shaking > 0)
				{
					shaking -= Time.Delta;
					if (Time.OnInterval(0.05f))
						shake = new(rng.Sign(), rng.Sign());
				}
				else
					shake = Point2.Zero;
			}
			else
				hitStun -= Time.Delta;
		}
		// Lerp the Camera to the new Room
		else if (nextRoomEase < 1.0f)
		{
			Camera = Vector2.Lerp(room.WorldBounds.TopLeft, nextRoom.WorldBounds.TopLeft, Ease.Cube.InOut(nextRoomEase));
			nextRoomEase = Calc.Approach(nextRoomEase, 1.0f, Time.Delta * 4.0f);
		}
		// Finished Lerping the Camera to the new room, return to normal update
		else
		{
			room = nextRoom;
			nextRoom = null;
			ApplyMusicArea(room);
			HitStun(0.1f);
		}

		UpdateAudioListener();
	}

	public void Render(in RectInt viewport)
	{
		// draw gameplay to screen
		Screen.Clear(0x150e22);
		Batcher.PushMatrix(-(Point2)Camera + shake);

		// draw actors
		rendering.AddRange(Actors);
		rendering.Sort((a, b) => b.Depth - a.Depth);
		foreach (var actor in rendering)
		{
			if (!actor.Visible)
				continue;

			Batcher.PushMatrix(actor.Position);
			actor.Render(Batcher);
			Batcher.PopMatrix();
		}
		rendering.Clear();

		if (showCollisionRects)
			DrawCollisionRects();

		Batcher.PopMatrix();

		// draw player HP
		if (GetFirst<Player>() is { } player)
		{
			Point2 pos = new(0, Height - 16);
			Batcher.Rect(new Rect(pos.X, pos.Y + 7, 48, 4), Color.Black);

			for (int i = 0; i < Player.MaxHealth; i++)
			{
				if (player.Health >= i + 1)
					Batcher.Image(Assets.GetSubtexture("heart/0"), pos, Color.White);
				else
					Batcher.Image(Assets.GetSubtexture("heart/1"), pos, Color.White);
				pos.X += 12;
			}
		}

		Batcher.Render(Screen);
		Batcher.Clear();

		// draw screen to window
		{
			var size = viewport.Size;
			var center = viewport.Center;
			var scale = Calc.Min(size.X / (float)Screen.Width, size.Y / (float)Screen.Height);

			Batcher.PushSampler(new(TextureFilter.Nearest, TextureWrap.Clamp, TextureWrap.Clamp));
			if (crtMaterial != null)
			{
				float CrtTime = Time.SecondsF * 0.2f;
				float Curvature = 0.005f;
				float BloomAmount = 0.19f;
				float ScanlineAmount = 0.07f;
				float VignetteAmount = 0.22f;
				float RgbOffset = 0.35f;
				crtMaterial.Fragment.SetUniformBuffer([
					Screen.Width,
					Screen.Height,
					CrtTime,
					BloomAmount,
					ScanlineAmount,
					VignetteAmount,
					RgbOffset,
					Curvature
				]);
				Batcher.PushMaterial(crtMaterial);
			}
			Batcher.Image(Screen, center, Screen.Bounds.Size / 2, Vector2.One * scale, 0, Color.White);
			if (crtMaterial != null)
				Batcher.PopMaterial();
			Batcher.PopSampler();
			Batcher.Render(Manager.Window);
			Batcher.Clear();
		}
	}

	private void DrawCollisionRects()
	{
		if (room == null)
			return;

		foreach (var rect in room.CollisionRects)
			Batcher.RectLine(rect, 1, Color.Red);
	}

	private Material? CreateCrtMaterial()
	{
		try
		{
			var ext = GraphicsDevice.Driver.GetShaderExtension();
			var path = Path.Join(Assets.AssetsPath, "Shaders", "Compiled", $"Crt.fragment.{ext}");
			if (!File.Exists(path))
			{
				Console.Error.WriteLine($"CRT shader not found at '{Path.GetFullPath(path)}'.");
				return null;
			}

			return new Material(
				vertexShader: null,
				fragmentShader: new Shader(
					GraphicsDevice,
					ShaderStage.Fragment,
					File.ReadAllBytes(path),
					samplerCount: 1,
					uniformBufferCount: 1,
					entryPoint: "fragment_main",
					name: "CrtFragment")
			);
		}
		catch(Exception e)
		{
			Console.Error.WriteLine($"Failed to load CRT shader: {e}");
			return null;
		}
	}

	public T Create<T>(Point2? position = null) where T : Actor, new()
	{
		var instance = new T
		{
			Game = this,
			Position = position ?? Point2.Zero
		};
		instance.Added();
		Actors.Add(instance);
		return instance;
	}

	public T? GetFirst<T>() where T : Actor
	{
		foreach (var it in Actors)
			if (it is T instance && !destroying.Contains(it))
				return instance;
		return null;
	}

	public Actor? GetFirst(Actor.Masks mask)
	{
		foreach (var it in Actors)
			if (it.Mask.Has(mask) && !destroying.Contains(it))
				return it;
		return null;
	}

	public void Destroy(Actor actor)
	{
		if (!destroying.Contains(actor))
			destroying.Add(actor);
	}

	public void PlayAudioEvent(string path, string parameterName = "", string parameterLabel = "", Vector2? position = null)
	{
		try
		{
			using var instance = CreateAudioEvent(path, position);
			if (instance == null)
				return;

			if (parameterName != "" || parameterLabel != "")
				instance.SetParameterLabel(parameterName, parameterLabel, true);

			instance.Start();
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
		}
	}

	public SoundInstance? CreateAudioEvent(string path, Vector2? position = null)
	{
		if (!Audio.Initialized)
			return null;

		try
		{
			var instance = Audio.CreateEvent($"event:/{path}");
			if (position is { } pos)
				SetAudioEventPosition(instance, pos);
			return instance;
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
			return null;
		}
	}

	public void SetAudioEventPosition(SoundInstance? instance, Vector2 position, Vector2? relativeTo = null)
	{
		if (instance == null || !Audio.Initialized)
			return;

		try
		{
			var effectivePosition = position;
			if (relativeTo is { } origin)
				effectivePosition = Camera + new Vector2(Width, Height) / 2 + position - origin;

			instance.Set3DAttributes(ToAudioPosition(effectivePosition));
		}
		catch(Exception e)
		{
			Console.WriteLine(e);
		}
	}

	private void UpdateAudioListener()
	{
		if (!Audio.Initialized)
			return;

		Audio.SetListenerAttributes(ToAudioPosition(Camera + new Vector2(Width, Height) / 2));
	}

	private static Vector3 ToAudioPosition(Vector2 position)
	{
		return new(-position.X / AudioPixelsPerUnit, position.Y / AudioPixelsPerUnit, 0);
	}

	public string GetSurfaceTypeBelow(Actor actor)
	{
		if (room == null || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return Factory.DefaultSurfaceType;

		var bounds = actor.Hitbox.Rect + actor.Position;
		var y = bounds.Bottom;
		var left = GetSurfaceTypeAtWorldPoint(new(bounds.Left + 1, y));
		var right = GetSurfaceTypeAtWorldPoint(new(bounds.Right - 1, y));

		if (left != Factory.DefaultSurfaceType)
			return left;
		if (right != Factory.DefaultSurfaceType)
			return right;
		return left;
	}

	private string GetSurfaceTypeAtWorldPoint(Point2 point)
	{
		if (room == null)
			return Factory.DefaultSurfaceType;

		var local = point - room.WorldBounds.TopLeft;
		var tile = new Point2(
			(int)MathF.Floor(local.X / (float)TileSize),
			(int)MathF.Floor(local.Y / (float)TileSize));

		if (tile.X < 0 || tile.Y < 0 || tile.X >= Columns || tile.Y >= Rows)
			return Factory.DefaultSurfaceType;

		if (Factory.Find(room.Tiles[tile.X, tile.Y]) is { SurfaceType: { } surfaceType })
			return surfaceType;

		return Factory.DefaultSurfaceType;
	}

	public void StopMusic(bool immediate = false) {
		if (music == null)
			return;

		try {
			music.Stop(immediate);
			music.Release();
		} catch (Exception e) {
			Console.Error.WriteLine($"Failed to stop background music: {e}");
		}

		music = null;
		musicArea = null;
	}

	public void RefreshMusic()
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (room == null)
			return;

		StartMusic();
		ApplyMusicArea(room);
	}

	private void StartMusic()
	{
		if (!Manager.MusicEnabled || !Audio.Initialized || music != null)
			return;

		try
		{
			music = Audio.CreateEvent(Manager.BackgroundMusicEventPath);
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, Manager.DefaultMusicArea, true);
			musicArea = Manager.DefaultMusicArea;
			music.Start();
		}
		catch (Exception e)
		{
			if (audioFailures.Add(Manager.BackgroundMusicEventPath))
				Console.Error.WriteLine($"Failed to start background music '{Manager.BackgroundMusicEventPath}': {e}");
			music = null;
			musicArea = null;
		}
	}

	private void ApplyMusicArea(Room room)
	{
		if (!Manager.MusicEnabled)
		{
			StopMusic(true);
			return;
		}

		if (string.IsNullOrEmpty(room.MusicArea))
			return;

		StartMusic();
		if (music == null || musicArea == room.MusicArea)
			return;

		try
		{
			music.SetParameterLabel(Manager.BackgroundMusicAreaParameter, room.MusicArea, true);
			musicArea = room.MusicArea;
		}
		catch (Exception e)
		{
			if (audioFailures.Add($"{Manager.BackgroundMusicAreaParameter}:{room.MusicArea}"))
				Console.Error.WriteLine($"Failed to set background music area '{room.MusicArea}': {e}");
		}
	}

	public bool Transition(Point2 direction)
	{
		if (room != null && nextRoom == null)
		{
			var nextCell = room.Cell + direction;
			if (Assets.Rooms.TryGetValue(nextCell, out nextRoom))
			{
				foreach (var it in Actors)
					if (it is not Player)
						Destroy(it);

				nextRoomEase = 0.0f;
				LoadRoom(nextRoom);
				ApplyMusicArea(nextRoom);
				return true;
			}
		}

		return false;
	}

	public bool OverlapsAll(in RectInt rect, Actor.Masks mask, List<Actor> results)
	{
		foreach (var actor in Actors)
		{
			var local = rect - actor.Position;
			if (actor.Mask.Has(mask) && actor.Hitbox.Overlaps(local))
				results.Add(actor);
		}

		return results.Count > 0;
	}

	public Actor? OverlapsFirst(in RectInt rect, Actor.Masks mask)
	{
		foreach (var actor in Actors)
		{
			var local = rect - actor.Position;
			if (actor.Mask.Has(mask) && actor.Hitbox.Overlaps(local))
				return actor;
		}

		return null;
	}

	public bool OverlapsRoomCollision(Actor actor, Point2 offset)
	{
		if (room == null || actor.Hitbox.Shape != Hitbox.Shapes.Rect)
			return false;

		var bounds = actor.Hitbox.Rect + actor.Position + offset;
		foreach (var rect in room.CollisionRects)
		{
			if (bounds.Overlaps(rect))
				return true;
		}

		return false;
	}

	public void ReloadRoom(bool restartMusic = false)
	{
		if (room == null) return;

		if (restartMusic)
			StopMusic(true);

		foreach (var actor in Actors)
			Destroy(actor);
		LoadRoom(room);

		if (restartMusic)
		{
			StartMusic();
			ApplyMusicArea(room);
		}
	}

	public void RefreshRoomTiles()
	{
		if (room == null)
			return;

		Grid? bg = null;
		Grid? fg = null;
		DualGrid? dual = null;
		foreach (var actor in Actors)
		{
			if (actor is DualGrid dualGrid)
			{
				dual = dualGrid;
				continue;
			}

			if (actor is not Grid grid)
				continue;

			if (grid.Mask.Has(Actor.Masks.Solid))
				fg = grid;
			else
				bg = grid;
		}

		if (bg == null || fg == null || dual == null)
			return;

		bg.Clear();
		fg.Clear();
		dual.Clear();

		for (var x = 0; x < Columns; x ++)
		for (var y = 0; y < Rows; y ++)
		{
			var tile = new Point2(x, y);
			if (Factory.Find(room.Tiles[x, y]) is { } entry)
				entry.Tile?.Invoke(tile, room, fg, bg, dual);
		}

		BuildDualTiles();
	}

	void BuildDualTiles()
	{

	}

	void BuildDualTilesBroken()
	{
		if (room == null)
			return;

		DualGrid? dual = null;
		foreach (var actor in Actors)
		{
			if (actor is DualGrid dualGrid)
			{
				dual = dualGrid;
				continue;
			}
		}

		if (dual == null) return;

		dual.Clear();
		if (Assets.GetTileset("grass-dual") is { } grassDual)
		    dual.Build(room, 'G', grassDual);
		if (Assets.GetTileset("castle-dual") is { }  castleDual)
		    dual.Build(room, '1', castleDual);
	}

	public void LoadRoom(Room room)
	{
		room.RebuildGeometry();

		var offset = room.WorldBounds.TopLeft;

		var bg = Create<Grid>(offset);
		bg.Depth = 10;

		var dual = Create<DualGrid>(offset);
		dual.Depth = 7;

		var fg = Create<Grid>(offset);
		fg.Mask = Actor.Masks.Solid;
		fg.Depth = 5;

		// loop over room grid placing objects
		for (var x = 0; x < Columns; x++)
		for (var y = 0; y < Rows; y++)
		{
			var tile = new Point2(x, y);
			var at = offset + tile * TileSize;

			if (Factory.Find(room.Tiles[x, y]) is { } entry)
			{
				entry.Spawn?.Invoke(at + entry.Offset, this);
				entry.Tile?.Invoke(tile, room, fg, bg, dual);
			}
		}

		BuildDualTiles();
	}

	public void HitStun(float time) => hitStun = MathF.Max(hitStun, time);

	public void Shake(float time) => shaking = MathF.Max(shaking, time);
}
