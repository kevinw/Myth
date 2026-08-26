#!/bin/sh
# C++ half of linux-cross-cc.sh, for CMake (the Ozz runtime). Links libc++ statically,
# so the .so needs nothing but libc on the target.
exec zig c++ -target "${MYTH_LINUX_TARGET:-x86_64-linux-gnu.2.36}" "$@"
