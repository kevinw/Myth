#!/usr/bin/env bash
# C++ half of linux-cross-cc.sh, for CMake (the Ozz runtime). Links libc++ statically, so
# the .so needs nothing but libc on the target.
MYTH_ZIG_DRIVER=c++ exec "$(dirname "$0")/linux-cross-cc.sh" "$@"
