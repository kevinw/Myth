// Runs at the start of every `cs` session (see DevScript.cs); edit, then `cs-reset` to pick up changes.
// Grow this whenever a query is clumsy. The comment above each helper is its `help` text.

// Sel cast to T, or null
T? Selected<T>() where T : Actor => Sel as T;
// every live actor of type T
IEnumerable<T> All<T>() where T : Actor => G!.Actors.OfType<T>();
// live actor by id
Actor? Id(string id) => G?.GetActorById(id);
// world position to room-local (what room files store)
Point2 Local(Point2 world) => world - (G?.CurrentRoom?.WorldBounds.TopLeft ?? default);
// distance between two actors' origins
float Dist(Actor a, Actor b) => ((Vector2)(a.Position - b.Position)).Length();
// closest actors to a world position
IEnumerable<Actor> Near(Vector2 at, int count = 5) =>
	G!.Actors.OrderBy(a => Vector2.DistanceSquared((Vector2)a.Position, at)).Take(count);
// closest actors to an actor
IEnumerable<Actor> Near(Actor actor, int count = 5) =>
	Near((Vector2)actor.Position, count + 1).Where(a => a != actor).Take(count);
// world center of an actor's hitbox
Vector2 Center(Actor a) => ((Rect)a.Hitbox.Rect + a.Position).Center;
// each puppet's sculpted head, GPU vs the CPU reference; with a directory, writes side-by-side PNGs
string VerifyHeads(string? directory = null) => string.Join("\n", All<RigActor>().Select((p, i) =>
	$"{i} {p.Position}: {p.visual.sculptedHead?.Verify(directory == null ? null : System.IO.Path.Join(directory, i.ToString())) ?? "no sculpted head"}"));
