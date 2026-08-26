#!/usr/bin/env bash
# Compiles and links linux-x64 from any host: zig cc is clang plus a bundled glibc sysroot.
# ILC emits a linux-x64 object file anywhere; only the link needs this.
#
# zig cc rejects -fuse-ld/-gz/-pie (it drives its own lld and defaults to PIE), and its lld
# collects ILC's __modules section under --gc-sections, leaving __start___modules undefined.
# Drop all four. MYTH_ZIG_DRIVER picks the language: linux-cross-cxx.sh sets it to c++.
args=()
for arg; do
  case $arg in -fuse-ld=*|-gz=*|-pie|-Wl,-pie|-Wl,--gc-sections) continue ;; esac
  args+=("$arg")
done
exec zig "${MYTH_ZIG_DRIVER:-cc}" -target x86_64-linux-gnu.2.36 "${args[@]}"
