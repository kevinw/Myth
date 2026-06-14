local out_dir = app.params["out-dir"] or "Assets/Tilesets"

local function join_path(a, b)
  return a .. "/" .. b
end

local function rgb(hex)
  hex = hex:gsub("#", "")
  return Color {
    r = tonumber(hex:sub(1, 2), 16),
    g = tonumber(hex:sub(3, 4), 16),
    b = tonumber(hex:sub(5, 6), 16),
    a = 255
  }
end

local function shade(base, delta)
  return Color {
    r = math.max(0, math.min(255, base.red + delta)),
    g = math.max(0, math.min(255, base.green + delta)),
    b = math.max(0, math.min(255, base.blue + delta)),
    a = base.alpha
  }
end

local function fill_tile(image, tile_x, tile_y, color)
  local ox = tile_x * 8
  local oy = tile_y * 8
  for y = 0, 7 do
    for x = 0, 7 do
      image:drawPixel(ox + x, oy + y, color)
    end
  end
end

local function px(image, tile_x, tile_y, x, y, color)
  image:drawPixel(tile_x * 8 + x, tile_y * 8 + y, color)
end

local function rect(image, tile_x, tile_y, x, y, w, h, color)
  for yy = y, y + h - 1 do
    for xx = x, x + w - 1 do
      px(image, tile_x, tile_y, xx, yy, color)
    end
  end
end

local function line_h(image, tile_x, tile_y, x, y, w, color)
  rect(image, tile_x, tile_y, x, y, w, 1, color)
end

local function line_v(image, tile_x, tile_y, x, y, h, color)
  rect(image, tile_x, tile_y, x, y, 1, h, color)
end

local function write_sprite(name, base_hex, painter)
  local sprite = Sprite(32, 16, ColorMode.RGB)
  sprite.filename = join_path(out_dir, name .. ".ase")
  sprite.gridBounds = Rectangle(0, 0, 8, 8)

  local layer = sprite.layers[1]
  layer.name = "Tiles"

  local image = Image(sprite.width, sprite.height, ColorMode.RGB)
  local base = rgb(base_hex)
  local dark = shade(base, -13)
  local darker = shade(base, -24)
  local light = shade(base, 14)
  local accent = rgb("7b8f86")

  for ty = 0, 1 do
    for tx = 0, 3 do
      fill_tile(image, tx, ty, base)
      painter(image, tx, ty, base, dark, darker, light, accent)
    end
  end

  sprite.cels[1].image = image
  sprite:saveAs(sprite.filename)
  sprite:close()
end

write_sprite("myth-back", "242638", function(image, tx, ty, base, dark, darker, light)
  local variant = tx + ty * 4
  if variant == 0 then
    line_h(image, tx, ty, 0, 3, 8, dark)
    px(image, tx, ty, 2, 1, light)
    px(image, tx, ty, 6, 6, darker)
  elseif variant == 1 then
    line_v(image, tx, ty, 5, 0, 5, dark)
    line_h(image, tx, ty, 2, 5, 5, darker)
    px(image, tx, ty, 1, 2, light)
  elseif variant == 2 then
    rect(image, tx, ty, 0, 0, 8, 1, darker)
    rect(image, tx, ty, 0, 7, 8, 1, dark)
    px(image, tx, ty, 4, 3, light)
  elseif variant == 3 then
    line_h(image, tx, ty, 1, 2, 5, dark)
    line_v(image, tx, ty, 2, 2, 5, darker)
    px(image, tx, ty, 6, 4, light)
  elseif variant == 4 then
    rect(image, tx, ty, 0, 0, 2, 8, darker)
    px(image, tx, ty, 4, 1, light)
    px(image, tx, ty, 5, 6, dark)
  elseif variant == 5 then
    rect(image, tx, ty, 6, 0, 2, 8, darker)
    line_h(image, tx, ty, 1, 6, 5, dark)
  elseif variant == 6 then
    line_h(image, tx, ty, 0, 1, 8, dark)
    line_h(image, tx, ty, 0, 4, 8, darker)
    px(image, tx, ty, 3, 6, light)
  else
    line_v(image, tx, ty, 1, 0, 8, dark)
    line_v(image, tx, ty, 6, 1, 6, darker)
    px(image, tx, ty, 4, 4, light)
  end
end)

write_sprite("myth-runes", "202431", function(image, tx, ty, base, dark, darker, light, accent)
  line_h(image, tx, ty, 0, 0, 8, dark)
  line_h(image, tx, ty, 0, 7, 8, darker)
  local variant = tx + ty * 4
  if variant == 0 then
    line_v(image, tx, ty, 3, 2, 4, accent)
    px(image, tx, ty, 4, 3, accent)
  elseif variant == 1 then
    line_h(image, tx, ty, 2, 2, 4, accent)
    line_v(image, tx, ty, 4, 2, 4, accent)
  elseif variant == 2 then
    line_v(image, tx, ty, 2, 1, 5, accent)
    line_h(image, tx, ty, 2, 5, 4, accent)
  elseif variant == 3 then
    px(image, tx, ty, 2, 2, accent)
    px(image, tx, ty, 3, 3, accent)
    px(image, tx, ty, 4, 4, accent)
    px(image, tx, ty, 5, 5, accent)
  elseif variant == 4 then
    rect(image, tx, ty, 2, 2, 4, 1, accent)
    rect(image, tx, ty, 2, 5, 4, 1, accent)
  elseif variant == 5 then
    line_v(image, tx, ty, 5, 1, 5, accent)
    px(image, tx, ty, 3, 2, accent)
    px(image, tx, ty, 4, 3, accent)
  elseif variant == 6 then
    line_h(image, tx, ty, 3, 2, 3, accent)
    line_v(image, tx, ty, 3, 2, 4, accent)
  else
    px(image, tx, ty, 3, 2, accent)
    px(image, tx, ty, 4, 2, accent)
    px(image, tx, ty, 2, 4, accent)
    px(image, tx, ty, 5, 4, accent)
  end
end)

write_sprite("myth-vines", "202637", function(image, tx, ty, base, dark, darker, light)
  local vine = rgb("4f665d")
  local leaf = rgb("6d8170")
  local variant = tx + ty * 4
  local x = variant % 4 + 1
  line_v(image, tx, ty, x, 0, 8, vine)
  if variant % 2 == 0 then
    px(image, tx, ty, x + 1, 2, leaf)
    px(image, tx, ty, x - 1, 5, leaf)
  else
    px(image, tx, ty, x - 1, 3, leaf)
    px(image, tx, ty, x + 1, 6, leaf)
  end
  line_h(image, tx, ty, 0, 7, 8, darker)
end)
