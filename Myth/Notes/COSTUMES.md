Costumes live in `Assets/Sources/Costumes/<id>.json`. Choose `CostumeId` on a rig actor; an empty selection leaves it uncostumed. Files hot-reload while the game runs. The gallery `big_rig_puppet` has `candy-bands`, `brass-boots`, and `thorn-tail` examples.

Each entry in `Pieces` targets body regions through `Bones`: `Trunk`, `Waist`, `Neck`, `Shoulder`, `UpperArm`, `Forearm`, `Hand`, `Hip`, `Thigh`, `Shin`, `Foot`, or `Tail`. Combine tags with commas. Add `Left` or `Right` to restrict the side; otherwise both sides match. Regions are shared across humanoids and AlienBug, including its four legs. Missing regions are simply unused.

- `Kind`: `Sleeve` (default), `Stud`, or `Spikes`.
- `From` / `To`: fraction along each matching bone, from parent to child (default 0–1).
- `Count`: number of bands, studs, or spikes (default 1). Repeated sleeves leave gaps.
- `Width`: multiplier of the underlying bone width (default 1).
- `Length`: spike height in bone widths; negative puts spikes on the opposite side in the projected view.
- `Color`: hex tint (default white). Without a surface this is a solid fill.
- `Surface`: optional existing surface ID; its texture and normals follow the piece, and `Color` tints its color pass.

Pieces draw over their parent bone in list order, before the next body part in depth order. They participate in normals, shadows, reflections, and visual bounds, but do not change combat geometry. Editing individual pieces currently uses the JSON file; the actor inspector selects the shared asset.
