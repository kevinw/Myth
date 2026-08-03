namespace Myth;

public interface IJsonUndoTarget
{
	string UndoId { get; }
	string CaptureJson();
	void ApplyJson(string json);
}

public sealed class JsonUndoManager
{
	private const int MaxUndoEntries = 50;
	private readonly List<JsonUndoEntry> undoStack = [];
	private readonly List<JsonUndoEntry> redoStack = [];
	private readonly Dictionary<string, (string Identity, JsonUndoScope Scope)> pending = [];

	public bool CanUndo => undoStack.Count > 0;
	public bool CanRedo => redoStack.Count > 0;
	public string? UndoName => CanUndo ? undoStack[^1].Name : null;
	public string? RedoName => CanRedo ? redoStack[^1].Name : null;

	public JsonUndoScope Capture(string name, params IJsonUndoTarget[] targets)
		=> new(this, name, targets);

	// Pending scopes: a multi-frame gesture (a drag, a paint stroke, a slider edit) opens a
	// scope under a slot at its first change and commits it when the gesture ends, so the whole
	// gesture becomes one undo entry. One scope per slot; opening a slot under a different
	// identity (e.g. another prefab's id) commits the previous scope first. Undo/Redo abandon
	// all pending scopes, since their before-states predate the rewind.

	public void OpenPending(string slot, string name, params IJsonUndoTarget[] targets)
		=> OpenPending(slot, "", name, targets);

	public void OpenPending(string slot, string identity, string name, params IJsonUndoTarget[] targets)
	{
		if (pending.TryGetValue(slot, out var entry) && entry.Identity == identity)
			return;

		CommitPending(slot);
		pending[slot] = (identity, new JsonUndoScope(this, name, targets));
	}

	public bool HasPending(string slot)
		=> pending.ContainsKey(slot);

	public bool HasPending(string slot, string identity)
		=> pending.TryGetValue(slot, out var entry) && entry.Identity == identity;

	public void CommitPending(string slot)
	{
		if (pending.Remove(slot, out var entry))
			entry.Scope.Commit();
	}

	public void AbandonPending()
		=> pending.Clear();

	public void Clear()
	{
		undoStack.Clear();
		redoStack.Clear();
		AbandonPending();
	}

	public void Undo()
	{
		AbandonPending();
		if (!CanUndo)
			return;

		var entry = undoStack[^1];
		undoStack.RemoveAt(undoStack.Count - 1);
		entry.Undo();
		redoStack.Add(entry);
	}

	public void Redo()
	{
		AbandonPending();
		if (!CanRedo)
			return;

		var entry = redoStack[^1];
		redoStack.RemoveAt(redoStack.Count - 1);
		entry.Redo();
		undoStack.Add(entry);
	}

	internal void Push(JsonUndoEntry entry)
	{
		undoStack.Add(entry);
		if (undoStack.Count > MaxUndoEntries)
			undoStack.RemoveAt(0);
		redoStack.Clear();
	}
}

public sealed class JsonUndoScope : IDisposable
{
	private readonly JsonUndoManager manager;
	private readonly string name;
	private readonly IJsonUndoTarget[] targets;
	private readonly string[] beforeJson;
	private bool committed;

	internal JsonUndoScope(JsonUndoManager manager, string name, IJsonUndoTarget[] targets)
	{
		this.manager = manager;
		this.name = name;
		this.targets = targets;
		beforeJson = new string[targets.Length];
		for (var i = 0; i < targets.Length; i ++)
			beforeJson[i] = targets[i].CaptureJson();
	}

	public void Commit()
	{
		if (committed)
			return;

		committed = true;
		var afterJson = new string[targets.Length];
		var changed = false;
		for (var i = 0; i < targets.Length; i ++)
		{
			afterJson[i] = targets[i].CaptureJson();
			changed |= beforeJson[i] != afterJson[i];
		}

		if (changed)
			manager.Push(new JsonUndoEntry(name, targets, beforeJson, afterJson));
	}

	public void Dispose()
		=> Commit();
}

internal sealed class JsonUndoEntry
{
	public readonly string Name;
	private readonly IJsonUndoTarget[] targets;
	private readonly string[] beforeJson;
	private readonly string[] afterJson;

	public JsonUndoEntry(string name, IJsonUndoTarget[] targets, string[] beforeJson, string[] afterJson)
	{
		Name = name;
		this.targets = [..targets];
		this.beforeJson = [..beforeJson];
		this.afterJson = [..afterJson];
	}

	public void Undo()
		=> Apply(beforeJson);

	public void Redo()
		=> Apply(afterJson);

	private void Apply(string[] states)
	{
		for (var i = 0; i < targets.Length; i ++)
			targets[i].ApplyJson(states[i]);
	}
}
