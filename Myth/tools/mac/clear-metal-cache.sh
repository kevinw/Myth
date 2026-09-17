#!/bin/sh
# Clears Myth's Metal shader/pipeline caches so the next run compiles cold.
# Useful for testing the cold-install hitch. Run with --no-warmup afterward
# to hit it at room entry instead of the boot loading screen.
set -e
# macOS keys the cache by bundle ID, which lives in app.json with the other shared app values.
bundle_id="$(plutil -extract bundleId raw -o - "$(dirname "$0")/../../app.json")"
# An empty ID would make the rm below delete the whole user cache directory.
if [ -z "$(echo "$bundle_id" | tr -d '[:space:]')" ]; then
	echo "app.json must set a non-empty bundleId" >&2
	exit 1
fi
cache="$(getconf DARWIN_USER_CACHE_DIR)$bundle_id"
if [ ! -d "$cache" ]; then
	echo "No Myth Metal cache found at $cache"
	exit 0
fi
rm -rf "$cache"
echo "Cleared: $cache"
