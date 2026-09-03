# Adding New Font Icons

Editor icons use Font Awesome Free Solid 6.7.2. The font is merged into the editor's ImGui font atlas at startup, with only the glyphs listed in `Source/Editor/EditorIcons.cs` included.

## Add an icon

1. Find the icon's Unicode codepoint on the Font Awesome site or in the metadata for our pinned version:

   ```sh
   curl -fsSL \
     https://raw.githubusercontent.com/FortAwesome/Font-Awesome/6.7.2/metadata/icons.json \
     | jq '.["plus"] | { unicode, styles, free }'
   ```

   Check that `solid` is available as a free style. We currently bundle only `EditorAssets/Fonts/fa-solid-900.ttf`.

2. Add a semantic constant to `Source/Editor/EditorIcons.cs`. For example, Font Awesome's `plus` icon is `F067`:

   ```csharp
   public const string Add = "\uf067";
   public const string AddButton = Add + "  Add";
   ```

3. Add the codepoint to `GlyphRanges`, keeping the entries sorted:

   ```csharp
   public static readonly ushort[] GlyphRanges =
   [
       0xf002, 0xf002, // Search
       0xf067, 0xf067, // Add
       0xf0e2, 0xf0e2, // Reset
       0
   ];
   ```

   Each pair is an inclusive `start, end` range and the final `0` terminates the list. For an isolated icon, repeat its codepoint as both the start and end. A wider range such as `0xf100, 0xf110` works, but it bakes every glyph in that range into the atlas, so prefer singleton ranges.

4. Use the semantic constant in the editor UI:

   ```csharp
   ImGui.Button(EditorIcons.AddButton);
   ```

   Invisible actors can use a named glyph instead of drawing a palette sprite. Add its
   name to `TryGetPaletteIcon`, then declare explicit editor bounds:

   ```csharp
   [GameActor(Icon = "speaker", BoundsX = -4, BoundsY = -4, BoundsW = 8, BoundsH = 8)]
   ```

5. Restart the editor. The font atlas is built once during startup, so hot reload cannot add a newly requested glyph to the existing atlas.

If the icon is only available in another Font Awesome style, that style's font must also be bundled and merged. If the icon list grows large, generate `EditorIcons.cs` from Font Awesome's metadata instead of maintaining it manually.
