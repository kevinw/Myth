#if !BROWSER
using System.Text.Json;
using Foster.Framework;

namespace Myth;

public partial class Manager
{
	public void WriteAllRooms()
	{
		var output = Path.GetFullPath(Path.Join(Assets.AssetsPath, "..", "Production", "Room Screenshots"));
		var worlds = Assets.GetWorldIds();
		var manifestPath = Path.Join(output, "rooms.json");
		Directory.CreateDirectory(output);

		var count = 0;
		using (var manifest = File.Create(manifestPath + ".tmp"))
		using (var json = new Utf8JsonWriter(manifest, new() { Indented = true, NewLine = "\n" }))
		{
			json.WriteStartObject();
			json.WriteNumber("version", 1);
			json.WriteNumber("tileSize", Game.TileSize);
			json.WriteStartArray("worlds");

			foreach (var world in worlds)
			{
				Assets.SelectWorld(world);
				Assets.ReloadRooms();
				Assets.RebuildRoomGeometry();
				var worldDirectory = Path.Join(output, world);
				Directory.CreateDirectory(worldDirectory);

				json.WriteStartObject();
				json.WriteString("id", world);
				json.WriteStartArray("rooms");
				foreach (var room in Assets.RoomsById.Values.OrderBy(it => it.Sublayer).ThenBy(it => it.Id, StringComparer.OrdinalIgnoreCase))
				{
					var bounds = room.WorldBounds;
					var file = $"{room.Id}.png";
					var path = Path.Join(worldDirectory, file);
					using var target = new Target(GraphicsDevice, bounds.Width, bounds.Height, name: $"RoomExport/{world}/{room.Id}");
					target.Clear(Color.Transparent);
					using (var roomGame = new Game(this, room, audioEventsEnabled: false, spawnBud: false, enableParticles: false, loadGoals: false))
						roomGame.RenderWorldToTarget(target, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));

					var pixels = new Color[target.Width * target.Height];
					target.Attachments[0].GetData<Color>(pixels);
					using (var image = new Image(target.Width, target.Height, pixels))
						image.WritePng(path);

					json.WriteStartObject();
					json.WriteString("id", room.Id);
					json.WriteNumber("sublayer", room.Sublayer);
					json.WriteNumber("x", bounds.X);
					json.WriteNumber("y", bounds.Y);
					json.WriteNumber("width", bounds.Width);
					json.WriteNumber("height", bounds.Height);
					json.WriteString("file", $"{world}/{file}");
					json.WriteEndObject();
					count++;
				}
				json.WriteEndArray();
				json.WriteEndObject();
				Console.WriteLine($"Room export ({world}): {Assets.RoomsById.Count} PNGs to {worldDirectory}");
			}

			json.WriteEndArray();
			json.WriteEndObject();
			json.Flush();
			manifest.Flush(true);
		}
		File.Move(manifestPath + ".tmp", manifestPath, true);
		Console.WriteLine($"Room export complete: {count} rooms across {worlds.Count} worlds; {manifestPath}");
	}
}
#endif
