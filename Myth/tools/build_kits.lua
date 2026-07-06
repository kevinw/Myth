-- Builds the procedural dual-tileset kits (ruin, ice, ember) in the same format
-- as the hand-made stone/castle/dirt kits:
--   fill slice (0,0 32x8): four 8x8 interior variants
--   edge slice (0,8 32x8): four 8x8 rim tiles (noise)
--   mask slice (32,0 32x32): dual-grid silhouette, copied from stone-kit
--   invisible 'height' layer mirroring fill geometry (luminance = height)
--
-- Usage:
--   aseprite -b Assets/Tilesets/stone-kit.ase \
--     --script-param out-dir=Assets/Tilesets --script tools/build_kits.lua
--
-- Deterministic: each kit reseeds its own LCG, so rebuilds are byte-stable.

local stone = app.sprite -- opened stone kit, source of the mask blob
local outDir = app.params["out-dir"]

local function rgba(r, g, b)
  return app.pixelColor.rgba(r, g, b, 255)
end

local HI = rgba(155, 173, 183) -- height: raised
local LO = rgba(0, 0, 0)       -- height: recessed

-- Fill variants are 8x8 ASCII, keyed into kit.colors / kit.heights.
-- Edge tiles are weighted noise: { {color, cumulativeThreshold}, ... }.
local kits = {
  {
    name = "ruin",
    seed = 0x2F6E2B1,
    colors = {
      a = rgba(50, 60, 57),    -- dark grey-green block body
      b = rgba(75, 105, 47),   -- moss in the seams
      c = rgba(34, 32, 52),    -- deep carved recess
    },
    heights = { a = HI, b = LO, c = LO },
    edge = {
      { rgba(143, 151, 74), 0.42 },  -- weathered khaki stone
      { rgba(105, 106, 106), 0.80 }, -- grey stone
      { rgba(75, 105, 47), 0.92 },   -- dark moss fleck
      { rgba(153, 229, 80), 1.00 },  -- bright moss glint
    },
    -- Horizontal seams on rows 1 and 5 in every variant so courses line up
    -- across neighboring tiles; vertical joints vary per variant.
    fills = {
      {
        "aabaaaaa",
        "bbabbbbb",
        "aaaaabaa",
        "aaaaabaa",
        "aaaaabaa",
        "bbbbbabb",
        "aabaaaaa",
        "aabaaaaa",
      },
      {
        "aaaaabaa",
        "bbbbbabb",
        "abaaaaaa",
        "abaaccaa",
        "abaaccaa",
        "babbbbbb",
        "aaaaabaa",
        "aaaaabaa",
      },
      {
        "aaaabaaa",
        "bbbbabbb",
        "aabaaaaa",
        "aabaaaaa",
        "aabaaaaa",
        "bbabbbbb",
        "aaaaaaba",
        "aaaaaaba",
      },
      {
        "abaaaaab",
        "babbbbba",
        "aaaabaaa",
        "acaabaaa",
        "aaaabaaa",
        "bbbbabbb",
        "aaabaaaa",
        "aaabaaaa",
      },
    },
  },
  {
    name = "ice",
    seed = 0x51CEB19,
    colors = {
      a = rgba(48, 96, 130),   -- deep glacial blue
      b = rgba(63, 63, 116),   -- facet shear line
      c = rgba(95, 205, 228),  -- bright frozen chip
    },
    heights = { a = HI, b = LO, c = rgba(255, 255, 255) },
    edge = {
      { rgba(203, 219, 252), 0.42 }, -- pale frost
      { rgba(95, 205, 228), 0.80 },  -- cyan ice
      { rgba(48, 96, 130), 0.92 },   -- deep blue pocket
      { rgba(255, 255, 255), 1.00 }, -- white sparkle
    },
    -- All variants share the same diagonal phase ((x+y)%4==1) so the shear
    -- lines run unbroken across tile boundaries; variants differ in chips.
    fills = {
      {
        "abaaacaa",
        "baaabaaa",
        "aacbaaab",
        "aabaaaba",
        "abaaabaa",
        "baaabaaa",
        "aaabaaab",
        "aabaaaba",
      },
      {
        "abaaabaa",
        "caaabaaa",
        "aaabaaab",
        "aabaaaba",
        "abaaacaa",
        "baaabaaa",
        "aaabaacb",
        "aabaaaba",
      },
      {
        "abaaabaa",
        "baaabaaa",
        "aaacaaab",
        "aabaaaba",
        "abaaabaa",
        "baaabaaa",
        "aaabaaab",
        "aabaaaca",
      },
      {
        "abaaabaa",
        "baaacaba",
        "aaabaaab",
        "aabaaaca",
        "acaaabaa",
        "baaabaaa",
        "aaabaaab",
        "aabaaaba",
      },
    },
  },
  {
    name = "ember",
    seed = 0xE3B0C44,
    colors = {
      a = rgba(34, 32, 52),    -- volcanic glass
      m = rgba(69, 40, 60),    -- cooled mottling
      r = rgba(172, 50, 50),   -- magma crack
      o = rgba(223, 113, 38),  -- hot spot
    },
    heights = { a = HI, m = rgba(105, 106, 106), r = LO, o = LO },
    edge = {
      { rgba(89, 86, 82), 0.45 },   -- charcoal rim
      { rgba(69, 40, 60), 0.83 },   -- dark maroon
      { rgba(172, 50, 50), 0.93 },  -- ember fleck
      { rgba(223, 113, 38), 1.00 }, -- hot fleck
    },
    -- Cracks stay clear of tile borders so random variants never produce
    -- severed glowing veins at seams.
    fills = {
      {
        "aamaaaaa",
        "aamaaaam",
        "aaaraaam",
        "aaaaraaa",
        "maaaroaa",
        "maaaaraa",
        "aaaaaara",
        "amaaaaam",
      },
      {
        "aaaammaa",
        "aaaaamaa",
        "maaaaaaa",
        "mmaaamaa",
        "aaaaamma",
        "aamaaaaa",
        "aamaaaam",
        "aaaaaaam",
      },
      {
        "aaaaamaa",
        "maaaamaa",
        "maaaaaaa",
        "aarraaam",
        "aaaarora",
        "amaaaaaa",
        "amaaaama",
        "aaaaaama",
      },
      {
        "aaaaaaaa",
        "amaaaraa",
        "amaaaaoa",
        "aaaaaara",
        "aammaaaa",
        "maaaamaa",
        "aaaaamaa",
        "aamaaaaa",
      },
    },
  },
}

local stoneMaskCel = nil
for _, layer in ipairs(stone.layers) do
  if layer.name == "mask" then stoneMaskCel = layer:cel(1) end
end
assert(stoneMaskCel, "stone kit has no mask layer cel")

for _, kit in ipairs(kits) do
  local seed = kit.seed
  local function rand()
    seed = (seed * 1103515245 + 12345) % 2147483648
    return seed / 2147483648
  end
  local function edgePixel()
    local r = rand()
    for _, entry in ipairs(kit.edge) do
      if r < entry[2] then return entry[1] end
    end
    return kit.edge[#kit.edge][1]
  end

  local spr = Sprite(64, 32, ColorMode.RGB)
  local artLayer = spr.layers[1]
  artLayer.name = "Layer 1"

  local art = Image(64, 32, ColorMode.RGB)
  for i, tile in ipairs(kit.fills) do
    local ox = (i - 1) * 8
    for y = 0, 7 do
      local row = tile[y + 1]
      for x = 0, 7 do
        local key = row:sub(x + 1, x + 1)
        art:putPixel(ox + x, y, assert(kit.colors[key], kit.name .. " fill key " .. key))
      end
    end
  end
  for x = 0, 31 do
    for y = 8, 15 do
      art:putPixel(x, y, edgePixel())
    end
  end
  spr:newCel(artLayer, 1, art, Point(0, 0))

  local heightLayer = spr:newLayer()
  heightLayer.name = "height"
  heightLayer.isVisible = false
  local heightImg = Image(64, 32, ColorMode.RGB)
  for i, tile in ipairs(kit.fills) do
    local ox = (i - 1) * 8
    for y = 0, 7 do
      local row = tile[y + 1]
      for x = 0, 7 do
        local key = row:sub(x + 1, x + 1)
        heightImg:putPixel(ox + x, y, assert(kit.heights[key], kit.name .. " height key " .. key))
      end
    end
  end
  spr:newCel(heightLayer, 1, heightImg, Point(0, 0))

  local maskLayer = spr:newLayer()
  maskLayer.name = "mask"
  spr:newCel(maskLayer, 1, stoneMaskCel.image:clone(), stoneMaskCel.position)

  local function addSlice(name, x, y, w, h)
    local slice = spr:newSlice(Rectangle(x, y, w, h))
    slice.name = name
  end
  addSlice("fill", 0, 0, 32, 8)
  addSlice("edge", 0, 8, 32, 8)
  addSlice("mask", 32, 0, 32, 32)

  local outPath = outDir .. "/" .. kit.name .. "-kit.ase"
  spr:saveAs(outPath)
  print("saved " .. outPath)
end
