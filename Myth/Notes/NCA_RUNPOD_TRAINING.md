# Training dynamic-texture NCAs on RunPod

For agents training `NeuralTextureActor` models (`Myth/tools/nca_lppn`) off the Mac. Last done 2026-09-12.

## Why and what to rent

| `reference` profile, 3000 epochs | Peak VRAM | Time per run |
|---|---|---|
| color+normal (`--normal auto`) | 29.7 GB | ~9 min on RTX 5090 |
| color only | 19.6 GB | ~10.4 min on RTX 5090 (incl. test video) |
| Mac M2 Max (MPS), color+normal | 36 GB footprint | 51 min |

- Rent a **Community RTX 5090 (32 GB, $0.69/hr)**. 16 GB cards OOM on `reference`. Several runs on one card OOM or barely add throughput — parallelize with more pods, not more processes. `--compile-siren` gains nothing.
- Stock comes and goes. If `create-pod` says no instances, retry every ~30 min.
- **Terminate, don't stop**, when done: a stopped Community pod usually can't restart ("not enough free GPUs on the host").

## Setup

RunPod MCP (hosted, OAuth): `claude mcp add --transport http runpod -s user https://mcp.getrunpod.io/`, then `/mcp` to sign in. The user's SSH public key must be on the RunPod account *before* the pod is created.

Create the pod with `create-pod` (state the hourly price to the user first):

```json
{"name": "nca-train", "cloud": "COMMUNITY",
 "gpu": {"id": "NVIDIA GeForce RTX 5090", "count": 1, "minCudaVersion": "12.8"},
 "image": "runpod/pytorch:1.0.2-cu1281-torch280-ubuntu2404", "disk": 30,
 "mounts": {"persistent": {"path": "/workspace", "size": 20}}, "ports": ["22/tcp"], "startSsh": true}
```

## Getting in

- If `get-pod` shows a public IP/port for 22, plain `ssh`/`scp` work. Commands run over SSH don't inherit the container env; prefix them with
  `export $(tr "\0" "\n" < /proc/1/environ | grep -E "^(PATH|PIP_[A-Z_]+|PYTHONUNBUFFERED)=" | xargs)` or pip fails with PEP 668.
- Many Community hosts have **no public port**; only `ssh <podid>-<suffix>@ssh.runpod.io` works. That proxy rejects non-PTY commands and ignores command strings, but you can pipe commands into an interactive shell:

  ```sh
  printf 'echo MARK-$((1+1)); nvidia-smi; echo END-$((2+2)); exit\n' \
    | perl -e 'alarm 60; exec @ARGV' ssh -tt -o BatchMode=yes -i ~/.ssh/id_ed25519 USER@ssh.runpod.io 2>/dev/null \
    | tr -d '\r' | awk '/MARK-2/{p=1;next} /END-4/{p=0} p'
  ```

  The `$((…))` markers keep the echoed command line from matching; don't anchor them (the first output line carries a `\e[?2004l` prefix). The web terminal truncates pastes at ~1 KB, so don't paste scripts there.

**Upload over the proxy** (base64 in 76-column lines, raw tty, exact byte count, checksum before use):

```sh
tar cf bundle.tar nca_lppn_pod.tgz run_motions.sh && base64 -b 76 -i bundle.tar -o bundle.b64
N=$(wc -c < bundle.b64 | tr -d ' '); SHA=$(shasum -a 256 bundle.tar | cut -d' ' -f1)
{ printf 'stty raw -echo; head -c %s | base64 -d > /workspace/bundle.tar; stty sane; echo "%s  /workspace/bundle.tar" | sha256sum -c && cd /workspace && tar xf bundle.tar && (TARGET=targets/photo.jpg NAME=photo setsid nohup bash run_motions.sh > run_motions.log 2>&1 < /dev/null &); sleep 20; exit\n' "$N" "$SHA"; sleep 3; cat bundle.b64; sleep 30; } \
  | perl -e 'alarm 570; exec @ARGV' ssh -tt -o BatchMode=yes -i ~/.ssh/id_ed25519 USER@ssh.runpod.io
```

**Download** the same way in reverse: print `sha256sum *.bin`, then `tar cf - *.bin | base64 -w 76` between markers, decode locally, and `shasum -a 256 -c` before copying into `Myth/Assets/NCA/DynamicTexture/` (use `cp -n`). The `.bin` files are ~55 KB each. Skip the ~50 MB test videos over the proxy.

## Payload

`nca_lppn_pod.tgz`, built from `Myth/tools/nca_lppn`, extracts to `/workspace/myth/tools/nca_lppn` so `train.py` exports to `/workspace/myth/Assets/NCA/DynamicTexture`:

```sh
tar czf nca_lppn_pod.tgz train.py export_model.py import_model.py configs/bubbly_circular.yaml \
  targets/bubbly_0101.jpg targets/<your target(s)> data/pretrained/two_stream/two_stream_optic_flow.pth
```

Non-square photos are fine; the trainer center-crops. For normals, generate `targets/generated/<name>_color.png`/`_normal.png` locally first (`generate_normals.py --size 1024`, needs macOS).

`run_motions.sh` (trains every named motion; skips ones already exported; `TRAIN_ARGS="--normal auto"` for paired runs with `TARGET=targets/generated/<name>_color.png NAME=<name>_color+normal`):

```bash
#!/usr/bin/env bash
set -uo pipefail
export $(tr "\0" "\n" < /proc/1/environ | grep -E "^(PATH|PIP_[A-Z_]+|PYTHONUNBUFFERED)=" | xargs)
TARGET=${TARGET:?} NAME=${NAME:?}
ROOT=/workspace/myth/tools/nca_lppn
WEIGHTS=/workspace/myth/Assets/NCA/DynamicTexture
mkdir -p "$ROOT/logs" && tar xzf /workspace/nca_lppn_pod.tgz -C "$ROOT" && cd "$ROOT" || exit 1
python3 -c "import torch; torch.zeros(1).cuda()" || { echo "CUDA unavailable on this host"; exit 1; }
python3 -m pip install -q gdown matplotlib moviepy==1.0.3 pyyaml torch-geometric tqdm pillow numpy || exit 1
python3 train.py --prepare-only || exit 1
for motion in ${MOTIONS:-circular circle diverge converge hyperbolic 2block_x 2block_y 3block 4block}; do
	name=${NAME}_$motion
	[ -f "$WEIGHTS/$name.bin" ] && continue
	echo "=== $motion started $(date +%T)"
	if python3 train.py "$TARGET" ${TRAIN_ARGS:-} --motion "$motion" --overwrite > "logs/$name.log" 2>&1; then
		echo "done $motion $(date +%T)"
	elif [ -f "experiments/$name/model.pth" ]; then
		python3 export_model.py "experiments/$name" "$WEIGHTS/$name.bin" >> "logs/$name.log" 2>&1 && echo "exported $motion (test failed)"
	else
		echo "FAILED $motion; see logs/$name.log"
	fi
done
echo "ALL DONE $(date +%T)"
```

## Watching and gotchas

- Poll `/workspace/run_motions.log` every few minutes from a background loop (`grep -q "ALL DONE"`, `pgrep -f "bash run_motions.sh"`, `nvidia-smi` utilization). `run_motions.sh` never exits on its own until all motions are tried.
- **Broken hosts exist.** On one, the container had `NVIDIA_VISIBLE_DEVICES=void`: `nvidia-smi` still listed the 5090, but torch hit "CUDA unknown error" and `train.py` silently fell back to CPU (18 s/epoch). The CUDA guard in the runner catches this. If it fires, terminate and create another pod.
- Check GPU utilization (~80–97%) and ~0.17 s/epoch in the log's tqdm line after the first minute.
