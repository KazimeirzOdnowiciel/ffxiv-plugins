"""Build the standalone browser preview from the plugin's own data and logic.

    python tools/make_demo.py

Reads Data/squares.json (the file the plugin embeds) plus preview/src/*, and writes a
single self-contained preview/bingo-demo.html. No network, no build step, no dependencies.
"""

from __future__ import annotations

import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "preview" / "src"


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8")


def inline_js(source: str) -> str:
    # A literal </script> inside an inline script would close the tag early.
    return source.replace("</script>", r"<\/script>")


def main() -> int:
    data_path = ROOT / "Data" / "squares.json"
    if not data_path.exists():
        print(f"missing {data_path}", file=sys.stderr)
        return 1

    payload = json.loads(read(data_path))
    squares = payload.get("squares", [])
    if len(squares) < 24:
        print(f"not enough squares: {len(squares)}", file=sys.stderr)
        return 1

    # The preview only needs the fields the generator reads.
    slim = [
        {
            "id": square["id"],
            "text": square["text"],
            "cat": square["cat"],
            "rarity": square["rarity"],
            "tags": square.get("tags", []),
        }
        for square in squares
    ]

    pool_json = json.dumps(slim, ensure_ascii=True, separators=(",", ":"))
    html = read(SRC / "template.html")
    html = html.replace("/*__POOL__*/[]", inline_js(pool_json), 1)
    html = html.replace("/*__CORE__*/", inline_js(read(SRC / "core.js")), 1)
    html = html.replace("/*__APP__*/", inline_js(read(SRC / "app.js")), 1)

    if "/*__" in html:
        print("unreplaced placeholder left in output", file=sys.stderr)
        return 1

    out = ROOT / "preview" / "bingo-demo.html"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(html, encoding="utf-8")

    size = out.stat().st_size
    print(f"wrote {out} ({size:,} bytes, {len(slim)} squares)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
