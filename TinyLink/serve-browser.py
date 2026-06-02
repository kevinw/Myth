#!/usr/bin/env python3
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from functools import partial
from pathlib import Path
import argparse


class DevHandler(SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cross-Origin-Resource-Policy", "same-origin")
        super().end_headers()


def main():
    parser = argparse.ArgumentParser(description="Serve the TinyLink browser build with directory indexes enabled.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8082)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path("bin/Debug/net10.0/browser-wasm/AppBundle"),
        help="Directory to serve. Defaults to the browser-wasm AppBundle.",
    )
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.exists():
        raise SystemExit(f"Build output not found: {root}\nRun: dotnet build -r browser-wasm")

    handler = partial(DevHandler, directory=str(root))
    server = ThreadingHTTPServer((args.host, args.port), handler)
    print(f"Serving {root}")
    print(f"http://{args.host}:{args.port}/")
    print("Directory indexes are enabled for folders without index.html.")
    server.serve_forever()


if __name__ == "__main__":
    main()
