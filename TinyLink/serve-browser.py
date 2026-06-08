#!/usr/bin/env python3
import argparse
import mimetypes
import sys
from pathlib import Path
from wsgiref.simple_server import WSGIServer, WSGIRequestHandler, make_server
from socketserver import ThreadingMixIn

try:
    from whitenoise import WhiteNoise
except ImportError:
    raise SystemExit("WhiteNoise is required. Install with: python3 -m pip install whitenoise")


class ThreadingWSGIServer(ThreadingMixIn, WSGIServer):
    daemon_threads = True


class QuietRequestHandler(WSGIRequestHandler):
    def log_message(self, format, *args):
        return


def missing_app(environ, start_response):
    start_response("404 Not Found", [("Content-Type", "text/plain; charset=utf-8")])
    return [b"Not found"]


def make_app(root: Path):
    return WhiteNoise(
        missing_app,
        root=str(root),
        index_file=True,
        autorefresh=True,
        max_age=0,
        add_headers_function=add_dev_headers,
    )


def add_dev_headers(headers, path, url):
    headers["Cache-Control"] = "no-cache"
    headers["Cross-Origin-Opener-Policy"] = "same-origin"
    headers["Cross-Origin-Embedder-Policy"] = "require-corp"
    headers["Cross-Origin-Resource-Policy"] = "same-origin"


def main():
    parser = argparse.ArgumentParser(description="Serve the TinyLink browser build.")
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

    mimetypes.add_type("application/wasm", ".wasm")
    mimetypes.add_type("application/octet-stream", ".pdb")
    mimetypes.add_type("text/plain", ".symbols")

    app = make_app(root)
    with make_server(args.host, args.port, app, server_class=ThreadingWSGIServer, handler_class=QuietRequestHandler) as server:
        print(f"Serving {root}")
        print(f"http://{args.host}:{args.port}/")
        print("Static files served by WhiteNoise with Cache-Control: no-cache.")
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            print("\nStopped.")
            return 0

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
