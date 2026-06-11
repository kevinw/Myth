#!/usr/bin/env python3
# Patches a SPIR-V module (compute shaders only) so that storage images
# (RWTexture2D, etc.) that are never read or never written get NonReadable /
# NonWritable decorations. dxc doesn't emit these, but naga needs them to
# produce WGSL `texture_storage_2d<format, write>` instead of `read_write`,
# which is required for broad WebGPU support.
#
# Usage: fix_storage_image_access.py <input.spv> <output.spv>
# Requires spirv-dis and spirv-as on PATH.

import re
import subprocess
import sys


def main():
    src, dst = sys.argv[1], sys.argv[2]
    asm = subprocess.run(["spirv-dis", src], capture_output=True, check=True, text=True).stdout
    lines = asm.splitlines()

    # Find storage image type names (Sampled operand == 2).
    storage_image_types = set()
    for line in lines:
        m = re.match(r"\s*(%\S+) = OpTypeImage \S+ \S+ \S+ \S+ \S+ 2 ", line)
        if m:
            storage_image_types.add(m.group(1))

    if not storage_image_types:
        if src != dst:
            subprocess.run(["cp", src, dst], check=True)
        return

    # Find UniformConstant variables of those image types.
    image_vars = {}  # var name -> image type
    for line in lines:
        m = re.match(r"\s*(%\S+) = OpVariable (%_ptr_UniformConstant_\S+) UniformConstant", line)
        if not m:
            continue
        var, ptr_type = m.group(1), m.group(2)
        # Find the pointee type from the pointer type declaration.
        ptr_decl = next((l for l in lines if l.strip().startswith(f"{ptr_type} = OpTypePointer")), None)
        if not ptr_decl:
            continue
        pointee = ptr_decl.split()[-1]
        if pointee in storage_image_types:
            image_vars[var] = pointee

    if not image_vars:
        if src != dst:
            subprocess.run(["cp", src, dst], check=True)
        return

    # Map each loaded SSA value back to the image variable it was loaded from.
    loaded_from = {}  # ssa id -> var name
    for line in lines:
        m = re.match(r"\s*(%\S+) = OpLoad %\S+ (%\S+)", line)
        if m and m.group(2) in image_vars:
            loaded_from[m.group(1)] = m.group(2)

    read_vars = set()
    write_vars = set()
    for line in lines:
        m = re.match(r"\s*(?:%\S+ = )?OpImageRead\b.*?(%\S+)", line)
        if m:
            tokens = line.split()
            img = tokens[tokens.index("OpImageRead") + (2 if "=" in line else 1)]
            if img in loaded_from:
                read_vars.add(loaded_from[img])
        m = re.match(r"\s*OpImageWrite\b", line)
        if m:
            tokens = line.split()
            img = tokens[1]
            if img in loaded_from:
                write_vars.add(loaded_from[img])

    new_decorations = []
    for var in image_vars:
        if var not in read_vars:
            new_decorations.append(f"OpDecorate {var} NonReadable")
        if var not in write_vars:
            new_decorations.append(f"OpDecorate {var} NonWritable")

    if new_decorations:
        # Insert after the last existing OpDecorate/OpMemberDecorate line.
        last_decorate = max(i for i, l in enumerate(lines) if re.match(r"\s*OpDecorate|\s*OpMemberDecorate", l))
        for decoration in reversed(new_decorations):
            lines.insert(last_decorate + 1, decoration)

    patched_asm = "\n".join(lines) + "\n"
    subprocess.run(["spirv-as", "-o", dst], input=patched_asm, check=True, text=True)


if __name__ == "__main__":
    main()
