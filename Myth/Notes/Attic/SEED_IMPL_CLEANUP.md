# Seed implementation cleanup

Stable visual-variation hashing is currently duplicated across `Factory`, `DualGrid`, and `GameView`.

Consolidate these implementations around one helper that accepts an already-resolved world tile plus a stable material key. Preserve existing hashes where changing them would reshuffle authored room visuals, then migrate room tiles, dual grids, slopes, moving platforms, and TileField variation deliberately.
