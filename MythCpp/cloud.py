# The cloud loop: export the C++ game as a zip for a cloud agent, then apply the *_changed_files.zip
# it sends back to this checkout, to build, run and compare.
#
#   python3 MythCpp/cloud.py export     ~/Downloads/FosterGames-cpp.zip, laid out as this repo
#   python3 MythCpp/cloud.py            pick a returned zip (newest first) and apply it, then build and
#                                       run it, flip to its A/B sibling, or go back to the export
#   python3 MythCpp/cloud.py undo       back to the export: restore what the applied zip changed
#
# Each export is kept as a commit (refs/cloud/exports/<time>) holding exactly the files zipped,
# uncommitted edits included: the agent's diff is against those. Every returned zip is a complete
# diff from its export, so applying one first restores whatever the previously applied zip changed,
# then writes the new one. Only those files are touched (any edits to them are overwritten); the
# index is left alone. Each applied zip is kept as a commit too, refs/cloud/zips/<name>, to git diff.

import glob, os, re, subprocess, sys, tempfile, time, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SELF = os.path.relpath(os.path.abspath(__file__), ROOT)
DOWNLOADS = os.path.expanduser("~/Downloads")
TOP = "FosterGames"
APPLIED = "refs/cloud/applied"
ASSETS = "Myth/Assets/"
# What the agent reads and edits, from git (the working tree's version).
TRACKED = ["MythCpp", "CppKit/build.py", "CppKit/kit", "MythJai/shaders/lighting.hlsl",
           "Myth/Platforms/FMOD/Native/linux-x64",
           *[ASSETS + p for p in ["Shaders/Crt.hlsl", "Shaders/PixelSort.hlsl", "Shaders/Common.hlsl", "Rooms",
             "Sources/Sprites", "Sources/Costumes", "Particles", "Rigs/ual2.ozzpack", "Rigs/ual2.takes.json",
             "Fonts/m3x6.ttf", "Fonts/dogica.ttf", "Voice/manifest.json", "Voice/voice-de.json",
             *["Sounds/FMOD/Build/Desktop/" + bank for bank in ["Master.bank", "Master.strings.bank", "SFX.bank"]]]]]
# What the build needs from outside git: Foster is a submodule, the rest are Myth's build products.
# Linux only (the agent's sandbox), and only the packed visuals the C++ game reads.
EXTRAS = {
    "Foster/tools/linux": "git submodule update --init",
    "Foster/Framework/Content/Shaders/Batcher.hlsl": "git submodule update --init",
    "Foster/Framework/Internal/Native/linux-x64/libSDL3.so": "git submodule update --init",
    **{ASSETS + "Packed/Visuals/" + f: "cd Myth && dotnet build" for f in ["manifest.bin", "atlas.qoi", "lighting-noise.qoi"]},
    "Myth/bin/publish/linux-x64/libmyth_ozz.so": "cd Myth && node tools/publish-linux.mjs",
}
AGENTS = """# Human Questions, in C++

An isolated C++ fork of Human Questions (code name Myth), a 2D platformer. The main game is C#, in a
larger repo; this is its C++ port and only what that needs, laid out as in that repo.

- MythCpp/: the game. game.h declares it all; main.cpp (the frame, camera and render passes), world.cpp
  (rooms, tiles, sprites), actors.cpp, pawn.cpp (the player, burrowing), rig.cpp (ozz-animated rigs),
  effects.cpp (particles, pixel sort, shadows), ui.cpp (input, FMOD audio, barks, HUD).
- CppKit/kit/: the reusable kit: an SDL_GPU batcher, arenas, pools, JSON, Aseprite and font loading.
- Myth/Assets/: rooms (json), sprites (.ase), rigs, particles, FMOD banks (sound effects; no music); and
  Packed/, the baked tile art.
- Foster/, Myth/bin/, Myth/Assets/Packed/ and Myth/Platforms/ are built elsewhere: read them, don't edit them.
- Put new scripts and sources in MythCpp/.

## Build and run

    MythCpp/build.sh                 build
    MythCpp/build.sh run [args]      build, then run

This export carries Linux tools only. Linux (Ubuntu 24.04 or Debian, x86_64) needs
`apt-get install -y g++ libfreetype-dev zlib1g-dev libvulkan1 mesa-vulkan-drivers xvfb curl`, plus
`xdotool imagemagick` for MythCpp/play-linux.sh, which drives the game with keys and takes screenshots.
The first build fetches SDL3's headers (Debian's libsdl3-dev .deb). mesa-vulkan-drivers is the software
Vulkan (lavapipe) for machines without a GPU; without root, `run` fetches its .deb instead, which only
loads if Mesa's dependencies (libLLVM, libdrm...) are already installed. Without a DISPLAY, `run` uses
Xvfb. The .deb URLs in build.sh are pinned: if one 404s, bump its version.

Arguments, combinable:
- `shot <png> [frame]`: save the view at that frame (default 90) and quit; e.g. `run demo shot /tmp/a.png 900`
- `demo`: scripted input. What it logs is deterministic, so diff it before and after a change.
- `trace`: log what the player does. `room <path under Myth/Assets/Rooms>`, `at <x,y>`: where to start.
  `hold <SDL key name, e.g. Right>`: as if held from the start.
- `--no-lighting`: skip the lighting pass, about 3x faster on software Vulkan.

CppKit/build.py and MythCpp/build.py are the macOS hot-reload build (jet-live); ignore them.

## Style

- C++20 as "C with classes"; the STL only where it clearly wins. No exceptions or RTTI.
- Nothing allocates per frame: the `temp` arena resets each frame, `persist` lasts; actors live in Pool<T, N>.
- Whole-vector arithmetic over per-component math. Tweakables live next to where they're used.
- Lazy senior dev: fewest lines and files, deletion over addition. Comment only what the code can't say,
  matching the density around it.
"""
README = """# Human Questions, in C++

The C++ port of a 2D platformer. Agents: read [AGENTS.md](AGENTS.md) first; it says how to build and run.
"""
GITIGNORE = "MythCpp/build/\nMythCpp/build-linux/\nMythCpp/third_party/\nCppKit/third_party/\n__pycache__/\n.DS_Store\n"
ROOT_FILES = {"AGENTS.md": AGENTS, "README.md": README, ".gitignore": GITIGNORE}  # the zip's own, not the repo's


def git(*args, env=None, input=None, text=True):
    result = subprocess.run(["git", *args], cwd=ROOT, env=env, input=input, capture_output=True, text=text)
    if result.returncode:
        raise RuntimeError(f"git {' '.join(args)}: {result.stderr.strip() if text else result.stderr.decode().strip()}")
    return result.stdout.strip() if text else result.stdout


def temp_index():
    return {**os.environ, "GIT_INDEX_FILE": os.path.join(tempfile.mkdtemp(), "index")}


def export():
    missing = [(path, fix) for path, fix in EXTRAS.items() if not os.path.exists(os.path.join(ROOT, path))]
    for path, fix in missing:
        print(f"missing {path}: {fix}")
    if any(not path.endswith(".so") for path, _ in missing):
        sys.exit(1)

    env = temp_index()
    git("read-tree", "HEAD", env=env)
    git("add", "-A", "--", *TRACKED, env=env)
    commit = git("commit-tree", git("write-tree", env=env), "-p", "HEAD", "-m", "cloud export", env=env)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    git("update-ref", f"refs/cloud/exports/{stamp}", commit)

    files = [f for f in git("ls-tree", "-r", "-z", "--name-only", commit, "--", *TRACKED).split("\0") if f]
    for path, _ in EXTRAS.items():
        full = os.path.join(ROOT, path)
        if os.path.isdir(full):
            files += [os.path.relpath(os.path.join(d, f), ROOT) for d, _, fs in os.walk(full) for f in fs if f != ".DS_Store"]
        elif os.path.exists(full):
            files.append(path)
    out = os.path.join(DOWNLOADS, f"{TOP}-cpp.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for f in files:
            if ".git" not in f.split("/") and f != SELF:
                z.write(os.path.join(ROOT, f), f"{TOP}/{f}")
        for name, text in ROOT_FILES.items():
            z.writestr(f"{TOP}/{name}", text)
    print(f"exported {len(files)} files, {os.path.getsize(out) >> 20}MB, as of {stamp}: {out}")
    subprocess.run(["open", "-R", out])


# --- Applying ---

NAME = re.compile(r"^(?P<session>[0-9a-f-]{8,})_(?P<turn>\d+)(?:_(?P<variant>[a-z]))?_changed_files\.zip$")


def returned_zips():
    """(session, turn) -> {variant: path}, newest first."""
    groups = {}
    for path in sorted(glob.glob(os.path.join(DOWNLOADS, "*_changed_files.zip")), key=os.path.getmtime, reverse=True):
        m = NAME.match(os.path.basename(path))
        if m:
            groups.setdefault((m["session"], int(m["turn"])), {})[m["variant"] or ""] = path
    return groups


def base_of(diff):
    """The export whose files the diff starts from: every file it modifies must match its blob."""
    starts, path = [], None
    for line in diff.splitlines():
        if line.startswith("diff --git a/"):
            path = line.split(" b/")[0][len("diff --git a/"):].split("/", 1)[1]
        elif line.startswith("index ") and path:
            blob = line.split()[1].split("..")[0]
            if blob.strip("0"):
                starts.append((path, blob))
    for export in git("for-each-ref", "--sort=-refname", "--format=%(objectname)", "refs/cloud/exports").split():
        if all(exists(export, p) and git("rev-parse", f"{export}:{p}").startswith(b) for p, b in starts):
            return export
    return None


def exists(commit, path):
    return subprocess.run(["git", "cat-file", "-e", f"{commit}:{path}"], cwd=ROOT, capture_output=True).returncode == 0


def commit_for(path):
    """The zip as a commit: its export plus its diff, made in a scratch index."""
    diff = zipfile.ZipFile(path).read("changes.diff").decode()
    base = base_of(diff)
    if not base:
        raise RuntimeError("no export here matches the files this zip changed; was it made from another machine's export?")
    env = temp_index()
    git("read-tree", base, env=env)
    # -p2 drops a/ and the zip's top folder; the files the export adds at its root aren't the repo's.
    git("apply", "--cached", "-p2", *[f"--exclude={name}" for name in ROOT_FILES], "--whitespace=nowarn", env=env, input=diff)
    commit = git("commit-tree", git("write-tree", env=env), "-p", base, "-m", os.path.basename(path), env=env)
    git("update-ref", f"refs/cloud/zips/{os.path.basename(path)[:-len('.zip')]}", commit)
    return commit


def changed(commit):
    return set(git("diff", "--name-only", "-z", f"{commit}^", commit).split("\0")) - {""}


def switch_to(commit):
    """Restores what the applied zip changed to its export, then writes `commit`'s changes (None: just restore)."""
    applied = git("rev-parse", "--verify", "-q", APPLIED) if exists_ref(APPLIED) else None
    paths = (changed(applied) if applied else set()) | (changed(commit) if commit else set())
    source = commit or f"{applied}^"
    for p in sorted(paths):
        full = os.path.join(ROOT, p)
        if exists(source, p):
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "wb") as f:
                f.write(git("cat-file", "blob", f"{source}:{p}", text=False))
            os.chmod(full, 0o755 if git("ls-tree", source, "--", p).startswith("100755") else 0o644)
        elif os.path.exists(full):
            os.remove(full)
    if commit:
        git("update-ref", APPLIED, commit)
    elif applied:
        git("update-ref", "-d", APPLIED)
    return len(paths)


def exists_ref(ref):
    return subprocess.run(["git", "rev-parse", "--verify", "-q", ref], cwd=ROOT, capture_output=True).returncode == 0


def ask(prompt, choices, default):
    while True:
        answer = input(f"{prompt} [{default}] ").strip().lower() or default
        if answer in choices:
            return answer


def pick_zip():
    groups = list(returned_zips().items())[:9]
    if not groups:
        sys.exit(f"no *_changed_files.zip in {DOWNLOADS}")
    print()
    for i, ((session, turn), variants) in enumerate(groups, 1):
        when = time.strftime("%a %d %b %H:%M", time.localtime(max(map(os.path.getmtime, variants.values()))))
        ab = "/".join(v.upper() for v in sorted(variants) if v)
        print(f"  {i}) {session[:8]} turn {turn:<3} {ab:<5} {when}")
    (_, variants) = groups[int(ask("which zip?", [str(i) for i in range(1, len(groups) + 1)], "1")) - 1]
    if len(variants) == 1:
        return next(iter(variants.values())), variants
    return variants[ask(f"which variant, {'/'.join(sorted(variants))}?", list(variants), sorted(variants)[0])], variants


def apply():
    path, variants = pick_zip()
    while True:
        name = os.path.basename(path)
        try:
            commit = commit_for(path)
        except RuntimeError as e:
            print(f"\ncan't apply {name}:\n{e}")
            return
        n = switch_to(commit)
        stat = git("diff", "--stat", f"{commit}^", commit).splitlines()[-1].strip()
        print(f"\napplied {name}\n  {stat}; {n} files written in {ROOT}")
        variant = NAME.match(name)["variant"]
        others = sorted(v for v in variants if v != variant and v)
        while True:
            options = {"r": "build and run", **{v: f"switch to {v.upper()}" for v in others},
                       "d": "list its changes", "u": "undo: back to the export", "o": "another zip", "q": "quit"}
            print("  " + "   ".join(f"{k}) {label}" for k, label in options.items()))
            choice = ask(">", list(options), "r")
            if choice == "r":
                subprocess.run([os.path.join(ROOT, "MythCpp", "build.sh"), "run"])
            elif choice == "d":
                print(git("diff", "--stat=120", f"{commit}^", commit))
            elif choice == "u":
                print(f"restored {switch_to(None)} files to the export")
                return
            elif choice == "o":
                path, variants = pick_zip()
                break
            elif choice == "q":
                return
            else:
                path = variants[choice]
                break


if __name__ == "__main__":
    if sys.argv[1:] == ["export"]:
        export()
    elif sys.argv[1:] == ["undo"]:
        print(f"restored {switch_to(None)} files to the export")
    else:
        apply()
