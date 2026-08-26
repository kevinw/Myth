#!/bin/sh
# Cross-compiles/links linux-x64 from macOS: zig cc is clang plus a bundled glibc sysroot.
# ILC already emits a linux-x64 object file on any host; only the link needs this.
#
# Dropped flags: zig cc rejects -fuse-ld/-gz/-pie (it drives its own lld and defaults to
# PIE). --gc-sections has to go too — lld's start-stop-gc collects ILC's __modules
# section, leaving __start___modules undefined, and zig cc filters out the
# -z nostart-stop-gc that would prevent it. This is why the SDK links with bfd on Linux.
args=""
for arg in "$@"; do
  case "$arg" in
    -fuse-ld=*|-gz=*|-pie|-Wl,-pie|-Wl,--gc-sections) continue ;;
  esac
  args="$args \"$arg\""
done
eval exec zig cc -target "${MYTH_LINUX_TARGET:-x86_64-linux-gnu.2.36}" "$args"
