#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: generate-ios-icons.sh <source-png> <appiconset-dir>" >&2
  exit 2
fi

source_png="$1"
appiconset_dir="$2"

if [[ ! -f "$source_png" ]]; then
  echo "missing source icon: $source_png" >&2
  exit 1
fi

if ! command -v sips >/dev/null 2>&1; then
  echo "sips is required to generate iOS app icons" >&2
  exit 1
fi

mkdir -p "$appiconset_dir"
tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT
opaque_source="$tmpdir/AppIcon-opaque.png"

if ! python3 - "$source_png" "$opaque_source" <<'PY'
import struct
import sys
import zlib

source, dest = sys.argv[1], sys.argv[2]
png = open(source, "rb").read()
if png[:8] != b"\x89PNG\r\n\x1a\n":
    raise SystemExit("source icon is not a PNG")

pos = 8
width = height = bit_depth = color_type = interlace = None
idat = []

while pos < len(png):
    size = struct.unpack(">I", png[pos:pos + 4])[0]
    kind = png[pos + 4:pos + 8]
    data = png[pos + 8:pos + 8 + size]
    pos += size + 12
    if kind == b"IHDR":
        width, height, bit_depth, color_type, _, _, interlace = struct.unpack(">IIBBBBB", data)
    elif kind == b"IDAT":
        idat.append(data)
    elif kind == b"IEND":
        break

if bit_depth != 8 or color_type != 6 or interlace != 0:
    raise SystemExit("source icon must be an 8-bit non-interlaced RGBA PNG")

rgba_stride = width * 4
raw = zlib.decompress(b"".join(idat))
src = 0
previous = bytearray(rgba_stride)
rgb_rows = []

for _ in range(height):
    filter_type = raw[src]
    src += 1
    scanline = bytearray(raw[src:src + rgba_stride])
    src += rgba_stride
    row = bytearray(rgba_stride)

    for i, value in enumerate(scanline):
        left = row[i - 4] if i >= 4 else 0
        up = previous[i]
        up_left = previous[i - 4] if i >= 4 else 0

        if filter_type == 0:
            out = value
        elif filter_type == 1:
            out = value + left
        elif filter_type == 2:
            out = value + up
        elif filter_type == 3:
            out = value + ((left + up) // 2)
        elif filter_type == 4:
            p = left + up - up_left
            pa = abs(p - left)
            pb = abs(p - up)
            pc = abs(p - up_left)
            predictor = left if pa <= pb and pa <= pc else (up if pb <= pc else up_left)
            out = value + predictor
        else:
            raise SystemExit(f"unsupported PNG filter: {filter_type}")

        row[i] = out & 0xFF

    rgb = bytearray()
    for i in range(0, len(row), 4):
        rgb.extend(row[i:i + 3])
    rgb_rows.append(b"\x00" + bytes(rgb))
    previous = row

def chunk(kind, data):
    body = kind + data
    return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

out = bytearray(b"\x89PNG\r\n\x1a\n")
out += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
out += chunk(b"IDAT", zlib.compress(b"".join(rgb_rows), 9))
out += chunk(b"IEND", b"")
open(dest, "wb").write(out)
PY
then
  jpeg_source="$tmpdir/AppIcon-opaque.jpg"
  sips -s format jpeg "$source_png" --out "$jpeg_source" >/dev/null
  sips -s format png "$jpeg_source" --out "$opaque_source" >/dev/null
fi

make_icon() {
  local size="$1"
  local name="$2"
  sips -s format png -z "$size" "$size" "$opaque_source" --out "$appiconset_dir/$name" >/dev/null
}

make_icon 20 AppIcon-20.png
make_icon 29 AppIcon-29.png
make_icon 40 AppIcon-40.png
make_icon 58 AppIcon-29@2x.png
make_icon 60 AppIcon-20@3x.png
make_icon 76 AppIcon-76.png
make_icon 80 AppIcon-40@2x.png
make_icon 87 AppIcon-29@3x.png
make_icon 120 AppIcon-60@2x.png
make_icon 152 AppIcon-76@2x.png
make_icon 167 AppIcon-83.5@2x.png
make_icon 180 AppIcon-60@3x.png
make_icon 1024 AppIcon-1024.png
