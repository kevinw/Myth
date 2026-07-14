"""Runs the pinned Cells2Pixels dynamic-texture trainer for Myth."""

import argparse
import os
import subprocess
import sys
import urllib.request
from pathlib import Path


ROOT = Path(__file__).resolve().parent
UPSTREAM = ROOT / ".cells2pixels"
UPSTREAM_URL = "https://github.com/kevinw/Cells2Pixels.git"
UPSTREAM_REVISION = "a0a78a793a6c0a295348b8e3ae17dc14b7844a26"  # myth branch
TARGET_REVISION = "29342e2f92b2fd63133b985d34a41c20634248cd"
DEFAULT_TARGET = ROOT / "targets" / "bubbly_0101.jpg"
DEFAULT_TARGET_URL = (
    "https://raw.githubusercontent.com/Cells2Pixels/Cells2Pixels.github.io/"
    f"{TARGET_REVISION}/dynamic_texture_demo/target_images/textures_hr/bubbly_0101.jpg"
)


def prepare():
    if not UPSTREAM.exists():
        subprocess.run(["git", "clone", UPSTREAM_URL, str(UPSTREAM)], check=True)
        subprocess.run(["git", "-C", str(UPSTREAM), "checkout", "--detach", UPSTREAM_REVISION], check=True)
    else:
        revision = subprocess.run(
            ["git", "-C", str(UPSTREAM), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
        if revision != UPSTREAM_REVISION:
            raise RuntimeError(
                f"{UPSTREAM} is at {revision}; expected {UPSTREAM_REVISION}. "
                "Remove it and run this command again."
            )

    if not DEFAULT_TARGET.exists():
        DEFAULT_TARGET.parent.mkdir(parents=True, exist_ok=True)
        print(f"Downloading {DEFAULT_TARGET.name}")
        urllib.request.urlretrieve(DEFAULT_TARGET_URL, DEFAULT_TARGET)


def automatic_device(torch):
    if torch.cuda.is_available():
        return "cuda:0"
    if torch.backends.mps.is_available():
        return "mps"
    return "cpu"


def smoke_config(config):
    config["train"].update(
        epochs=1,
        batch_size=1,
        pool_size=2,
        step_range=[1, 2],
        summary_interval=1,
    )
    config["renderer"]["scale_factor"] = 1
    appearance = config["loss"]["appearance_loss_kwargs"]
    appearance.update(target_image_size=[64, 64], patch_size=[64, 64], num_scales=1, num_pseudo_targets=0)
    config["loss"]["motion_loss_kwargs"].update(image_size=[64, 64], base_num_steps=1)


def screen_config(config):
    config["renderer"]["scale_factor"] = 3
    config["loss"]["appearance_loss_kwargs"].update(
        target_image_size=[512, 512],
        patch_size=[160, 160],
        num_scales=2,
        num_pseudo_targets=0,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", type=Path, default=DEFAULT_TARGET)
    parser.add_argument("--normal", type=Path, help="Paired normal map; trains one model that outputs color and normal channels together")
    parser.add_argument("--motion", default="circular")
    parser.add_argument("--profile", choices=["reference", "screen"], default="reference")
    parser.add_argument(
        "--pseudo-targets",
        type=int,
        help="Override the profile's channel-permutation pseudo-target count "
        "(couples albedo/normal statistics in paired training)",
    )
    parser.add_argument("--device", default="auto")
    parser.add_argument("--epochs", type=int)
    parser.add_argument("--compile-siren", action="store_true", help="Compile the SIREN decoder on CUDA")
    parser.add_argument("--smoke", action="store_true", help="Run one reduced epoch to validate the toolchain")
    parser.add_argument("--prepare-only", action="store_true")
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--no-test", action="store_true")
    parser.add_argument("--test-steps", type=int, default=1024)
    parser.add_argument("--test-video-frames", type=int, default=240)
    parser.add_argument("--test-fps", type=float, default=30.0)
    args = parser.parse_args()

    prepare()
    if args.prepare_only:
        print(f"Prepared Cells2Pixels {UPSTREAM_REVISION} and {DEFAULT_TARGET}")
        return

    try:
        import torch
        import yaml
    except ImportError as error:
        raise SystemExit("Install requirements with: .venv/bin/pip install -r requirements.txt") from error

    target = args.target.resolve()
    if not target.is_file():
        raise SystemExit(f"Target image does not exist: {target}")

    config_path = ROOT / "configs" / "bubbly_circular.yaml"
    config = yaml.safe_load(config_path.read_text())
    device = automatic_device(torch) if args.device == "auto" else args.device
    if args.compile_siren and not device.startswith("cuda"):
        raise SystemExit("--compile-siren requires CUDA")
    config["device"] = device
    config["precision"] = "float16" if device.startswith("cuda") else "float32"
    config["loss"]["motion_loss_kwargs"]["target_name"] = args.motion
    stem = target.stem
    if args.normal is not None:
        normal = args.normal.resolve()
        if not normal.is_file():
            raise SystemExit(f"Normal map does not exist: {normal}")
        # Keys are sorted by the trainer: albedo gets channels 0-2, normal 3-5.
        config["num_channels"] = {"albedo": 3, "normal": 3}
        config["loss"]["appearance_loss_kwargs"]["target_images_path"] = {
            "albedo": str(target),
            "normal": str(normal),
        }
        stem += "+normal"
    else:
        config["loss"]["appearance_loss_kwargs"]["target_images_path"]["rgb"] = str(target)
    if args.profile == "screen":
        screen_config(config)
    if args.pseudo_targets is not None:
        config["loss"]["appearance_loss_kwargs"]["num_pseudo_targets"] = args.pseudo_targets

    name = f"{stem}_{args.motion}".replace("/", "_").replace("\\", "_")
    if args.profile != "reference":
        name += f"_{args.profile}"
    experiment = ROOT / "experiments" / name
    config["experiment_path"] = str(experiment)
    if args.epochs is not None:
        config["train"]["epochs"] = args.epochs
    if args.compile_siren:
        config["train"]["compile_siren"] = True
    if args.smoke:
        smoke_config(config)

    generated = ROOT / ".generated" / f"{name}.yaml"
    generated.parent.mkdir(parents=True, exist_ok=True)
    generated.write_text(yaml.safe_dump(config, sort_keys=False))

    command = [sys.executable, str(UPSTREAM / "train.py"), "--config", str(generated)]
    if args.overwrite:
        command.append("--overwrite")
    if not args.no_test:
        command.extend(
            [
                "--test",
                "--test-output-dir",
                str(ROOT / "outputs" / name),
                "--test-steps",
                str(args.test_steps),
                "--test-video-frames",
                str(args.test_video_frames),
                "--test-fps",
                str(args.test_fps),
            ]
        )

    print(f"Training {target.name} with motion '{args.motion}', profile '{args.profile}', on {device}")
    environment = os.environ.copy()
    if device == "mps":
        environment["PYTORCH_ENABLE_MPS_FALLBACK"] = "1"
    subprocess.run(command, cwd=ROOT, env=environment, check=True)
    print(f"Checkpoint: {experiment}")


if __name__ == "__main__":
    main()
