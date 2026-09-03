# Premultiplied Alpha

## Current convention

Foster's `Batcher` starts with `BlendMode.Premultiply`:

```text
result.rgb = source.rgb + destination.rgb * (1 - source.a)
```

This is a good default for sprite rendering. Premultiplied colors interpolate correctly at
transparent texture edges, avoiding the dark or colored fringes that straight-alpha images can
produce under filtering. Repeated compositing is also simpler and more stable.

It requires every ordinary color draw to supply RGB that has already been multiplied by alpha.
For example, 25%-opaque white is `(0.25, 0.25, 0.25, 0.25)`, not
`(1, 1, 1, 0.25)`.

## The `Color` footgun

These produce a valid premultiplied draw color:

```csharp
var faded = Color.White * opacity;
var tinted = new Color(0x63_9b_ff, opacity); // the int + float overload premultiplies RGB
var explicitTint = new Color(99, 155, 255, 255) * opacity;
```

These do not:

```csharp
var faded = Color.White;
faded.A = 64;                         // RGB remains 255
var tinted = new Color(99, 155, 255, 64); // byte RGBA constructor does not premultiply
var other = new Color(1f, 1f, 1f, 0.25f); // float RGBA constructor does not premultiply
```

Setting alpha alone happens to be safe for black because premultiplied black always has zero RGB.
That is why the black `TitleText` shadow works while doing the same thing to a white heart makes
only its black pixels appear to fade.

`Color * opacity` scales RGBA together and is the safest ordinary draw-time idiom. Four-channel
colors are still appropriate for shader data, normal buffers, masks, and other values that are not
being composited as color.

## Aseprite atlas mismatch

Myth's visible Aseprite frames currently appear to stay in straight-alpha form all the way to the
GPU:

1. `Aseprite.RenderFrame` produces ordinary RGBA pixels.
2. `Packer` copies those pixels into its page unchanged.
3. The development path uploads `output.Pages[0]` directly with `new Texture(...)`.
4. The packed path writes the page to PNG, decodes it into an `Image`, and uploads that unchanged.
5. Myth never calls Foster's existing `Image.Premultiply()` in either path.

Binary-alpha pixel art mostly conceals this mismatch. Partially transparent pixels, filtered
edges, translucent Aseprite layers, and opacity tints expose it.

Do **not** fix this by blindly calling `Premultiply()` on the completed atlas page. The same atlas
also packs normal maps and other shader-data subtextures whose RGB channels must not be multiplied
by coverage alpha. Premultiplication needs to happen selectively to visible color frames before
they enter the shared packer, or visible color data needs its own atlas/convention. Emissive frames
also need an explicit decision based on how their pass composites them.

The two upload sites worth keeping in view are the development atlas construction and packed-atlas
load in `Source/Assets/Assets.cs`. They should receive pixels with the same convention, but the
conversion probably belongs earlier, where color frames are still distinguishable from normals
and other data.

## Known suspicious call sites

This is not a complete audit, but these patterns deserve review:

- `Game.Rendering.cs`: the health tint mutates only `.A`; the HUD background and stamina colors use
  translucent four-byte constructors directly.
- `Manager.GameMenu.cs`: the enlarged player/Bud menu drawings mutate only `color.A`.
- `Manager.Editor.cs`: flagged-tile marker colors use straight float RGBA values with low alpha.
- `GameView.cs`: `ColorFromHsv` returns straight float RGBA for translucent debug drawing.
- `Player.Burrow.cs`: interval flash/warning colors use straight float RGBA values before drawing
  the glow.

Some visually similar sites are already correct:

- `new Color(0xRRGGBB, floatOpacity)` uses the premultiplying constructor.
- `opaqueColor * opacity` premultiplies by scaling every channel.
- translucent black remains valid when only alpha changes.
- colors written as render-target data rather than composited color do not follow this rule.

## Safe investigation path

1. Add a tiny diagnostic sprite with opaque, partially transparent, and fully transparent colored
   edge pixels. Draw it over light and dark backgrounds at fractional positions and several tints.
2. Capture desktop and browser baselines before changing the atlas pipeline.
3. Premultiply only visible color frames before packing, in both original and generated/in-between
   frame paths. Do not touch normal, height, mask, or packed-data images.
4. Audit premultiplied draw colors. In debug builds, `R <= A && G <= A && B <= A` is a useful smell
   test for colors sent through the default premultiplied blend.
5. Recheck custom materials and non-default blend scopes individually; additive, normal-buffer,
   mask, and data passes may intentionally use a different convention.

An alternative is to render straight-alpha atlas color sprites with
`BlendMode.NonPremultiplied`, but fonts are deliberately generated premultiplied and the existing
renderer assumes the premultiplied default broadly. Selectively premultiplying visible imagery is
likely the smaller and more coherent direction.
