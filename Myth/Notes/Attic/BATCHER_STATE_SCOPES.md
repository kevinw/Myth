# Batcher State Scopes

## Motivation

`Drone.RenderShapes` pushed a scale matrix without popping it. Because the `Batcher` is shared,
later draws inherited the transform based on render order. This appeared as unrelated failures:
ShieldKnight frames went partially invisible and reveal-door cover tiles rendered incorrectly.

The atlas and subtextures were valid. The leaked batcher state was the root cause.

## Proposed API

Add an allocation-free scoped form alongside the existing `PushMatrix` / `PopMatrix` API:

```csharp
using (batcher.PushMatrixScope(Matrix3x2.CreateScale(1.0f, 0.95f)))
{
	batcher.Circle(Vector2.Zero, BodyRadius, 16, Color.Violet);
	DrawEye(batcher);
}
```

`MatrixScope` should be a nested `readonly struct` holding the `Batcher`. Its `Dispose()` calls
`PopMatrix()`. The concrete struct used directly by `using` does not box or allocate. A `ref struct`
is unnecessary.

Keep `PushMatrix()` unchanged: it currently returns the resulting `Matrix3x2`, and changing its
return type would be a broad breaking change. Use a distinct name such as `PushMatrixScope()`.

Start with overloads for the common forms:

```csharp
public MatrixScope PushMatrixScope(in Matrix3x2 matrix, bool relative = true);
public MatrixScope PushMatrixScope(in Vector2 position, bool relative = true);
```

Prefer a block-scoped `using` over `using var`, since `using var` keeps the matrix pushed until the
end of the containing method and can accidentally widen the transform's lifetime.

## Debug Validation

Before `Batcher.Clear()` clears its state stacks, debug builds should assert that all stacks are
empty. Report the unbalanced stack names and depths so a leaked matrix, material, blend, sampler,
scissor, stencil, mode, or layer fails near the render pass where it occurred instead of silently
corrupting later draws.

`Clear()` should continue resetting all state afterward. The validation is diagnostic, not a change
to release behavior.

## Rollout

1. Add `MatrixScope` and the two common `PushMatrixScope()` overloads.
2. Add debug-only stack balance validation to `Batcher.Clear()`.
3. Use scoped matrices for new code and convert gameplay actor rendering when touched.
4. Do not mechanically convert every existing call site.
5. Add scoped forms for other state types only when their usage demonstrates the same need.

The scoped API prevents omissions at converted sites. The debug validation covers legacy code and
the other batcher state stacks without immediately adding a family of disposable scope types.
