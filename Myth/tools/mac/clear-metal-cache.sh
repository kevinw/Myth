#!/bin/sh
# Clears Myth's Metal shader/pipeline caches so the next run compiles cold.
# Useful for testing the cold-install hitch. Run with --no-warmup afterward
# to hit it at room entry instead of the boot loading screen.
set -e
cache="$(getconf DARWIN_USER_CACHE_DIR)/com.fostergames.myth"
if [ ! -d "$cache" ]; then
	echo "No Myth Metal cache found at $cache"
	exit 0
fi
rm -rf "$cache"
echo "Cleared: $cache"
