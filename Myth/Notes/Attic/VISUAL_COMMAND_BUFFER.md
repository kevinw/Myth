# Visual Command Buffer

The growing `RenderXXX` virtual family makes each Actor responsible for understanding the renderer's passes. This creates predictable problems:

- Overrides forget to call `base`.
- Visibility and ordering rules are repeated across passes.
- Every new visual channel adds another virtual method.
- Animation frames and transforms are resolved repeatedly.
- Rendering policy becomes distributed across gameplay classes.

Replace these methods with a visual command buffer. This does not require a full ECS.

## Proposed API

An Actor declares its visual composition once:

```csharp
public virtual void CollectVisuals(VisualCollector visuals)
{
	visuals.Sprite(new SpriteVisual
	{
		Frame = CurrentFrame,
		Transform = VisualTransform,
		Tint = Color.White,
		Features =
			VisualFeatures.Shadow |
			VisualFeatures.Reflection |
			VisualFeatures.EmissiveOccluder
	});
}
```

A `SpriteVisual` contains the complete authored surface:

- Color subtexture
- Normal subtexture
- Emissive subtexture
- Coverage subtexture, normally the color alpha
- Transform, tint and depth
- Feature flags
- Optional material

The renderer collects visuals once in depth order and interprets the same commands consistently for every pass.

| Pass | Visual data |
| --- | --- |
| Color | Color |
| Normals | Normal |
| Emissive | Erase using Coverage, then draw Emissive |
| Shadow | Coverage |
| Reflection | Color |
| Visibility | Coverage or an authored occluder |

This makes behavior such as emissive occlusion intrinsic to the renderer rather than special-case Actor logic.

## Custom visuals

Actors with multiple visual parts add multiple commands:

```csharp
public override void CollectVisuals(VisualCollector visuals)
{
	if (!BurrowHidesSprite)
		visuals.Sprite(PlayerVisual());

	if (ShowAura)
		visuals.Sprite(AuraVisual());

	if (Blinking)
		visuals.Sprite(BlinkVisual());
}
```

A `Light` with `DrawSprite == false` emits no sprite visual. Its illumination is a separate presentation command:

```csharp
visuals.Light(...);
```

Use a few concrete command types for unusual rendering rather than immediately introducing arbitrary callbacks:

- `SpriteVisual`
- `BeamVisual`
- `TileBatchVisual`
- `LightVisual`

The collector should use reusable scratch lists of struct commands, cleared each frame. Collection and pass execution should not allocate or use LINQ per frame.

## Migration

1. Add `CollectVisuals` and the command structs.
2. Convert base Actor, Prop and Light.
3. Make color, normal, emissive, shadow and reflection passes consume collected visuals.
4. Convert Player and bespoke enemies.
5. Delete each `RenderXXX` virtual after its final user is migrated.
6. Eventually move Grid and DualGrid out of Actor and treat terrain as retained tile batches.

During migration, legacy Actor rendering can remain as an adapter so this does not require a flag-day rewrite.

## Rejected shortcut

A single `Render(Batcher, RenderPass)` virtual would reduce the method count but preserve the underlying problem. Actors would still contain renderer-pass switches and rendering policy. Declarative visual commands provide the useful separation: Actors describe what is visible; the renderer decides how each pass consumes it.

## Possible later optimization

Once the visual model is stable, color, normal, emissive and coverage could potentially be emitted together through a small 2D G-buffer or multiple render targets. That is a renderer optimization, not a prerequisite for the command-buffer design, and should be evaluated separately for SDL GPU and WebGPU support.
