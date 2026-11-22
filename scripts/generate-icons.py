"""Generate multi-size PNG and multi-resolution ICO icons from the Quill SVG."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

DEFAULT_EDGE_PATHS = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
PNG_SIZES = [32, 64, 128, 256, 512]
RENDER_SIZE = 1024


def find_edge() -> Path:
    for candidate in DEFAULT_EDGE_PATHS:
        path = Path(candidate)
        if path.is_file():
            return path
    found = shutil.which("msedge")
    if found:
        return Path(found)
    raise FileNotFoundError("Microsoft Edge executable was not found.")


def render_svg_with_edge(svg_text: str, master_png: Path) -> None:
    edge = find_edge()
    html = f"""<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8" />
<style>
  html, body {{
    margin: 0;
    padding: 0;
    width: {RENDER_SIZE}px;
    height: {RENDER_SIZE}px;
    overflow: hidden;
    background: transparent;
  }}
  svg {{
    width: {RENDER_SIZE}px;
    height: {RENDER_SIZE}px;
    display: block;
  }}
</style>
</head>
<body>
{svg_text}
</body>
</html>
"""
    with tempfile.TemporaryDirectory(prefix="quill-icon-render-") as temp_dir:
        temp_path = Path(temp_dir)
        html_path = temp_path / "render.html"
        profile_dir = temp_path / "edge-profile"
        html_path.write_text(html, encoding="utf-8")

        command = [
            str(edge),
            "--headless=new",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-extensions",
            "--default-background-color=00000000",
            "--hide-scrollbars",
            "--force-device-scale-factor=1",
            f"--window-size={RENDER_SIZE},{RENDER_SIZE}",
            f"--user-data-dir={profile_dir}",
            f"--screenshot={master_png}",
            html_path.as_uri(),
        ]
        result = subprocess.run(command, capture_output=True, text=True, check=False)
        if result.returncode != 0 or not master_png.is_file():
            raise RuntimeError(
                f"Edge SVG render failed (exit {result.returncode}): {result.stderr.strip()}"
            )


def verify_master_rgba(master: Image.Image) -> Image.Image:
    rgba = master.convert("RGBA")
    if rgba.size != (RENDER_SIZE, RENDER_SIZE):
        rgba = rgba.resize((RENDER_SIZE, RENDER_SIZE), Image.Resampling.LANCZOS)

    w, h = rgba.size
    corners = [
        rgba.getpixel((0, 0)),
        rgba.getpixel((w - 1, 0)),
        rgba.getpixel((0, h - 1)),
        rgba.getpixel((w - 1, h - 1)),
    ]
    if any(pixel[3] != 0 for pixel in corners):
        raise RuntimeError(f"Expected transparent corners on rendered SVG, got {corners}")

    bbox = rgba.getbbox()
    if bbox is None:
        raise RuntimeError("Rendered SVG image is completely empty.")

    center = rgba.getpixel((w // 2, h // 2))
    print(f"Master RGBA size={rgba.size}, bbox={bbox}, center={center}, corners_alpha={[p[3] for p in corners]}")
    return rgba


def build_icons(input_svg: Path, output_dir: Path) -> None:
    if not input_svg.is_file():
        raise FileNotFoundError(f"Input SVG not found: {input_svg}")

    output_dir.mkdir(parents=True, exist_ok=True)
    svg_text = input_svg.read_text(encoding="utf-8")

    # Copy vector asset cleanly into Assets/Quill.svg.
    shutil.copy2(input_svg, output_dir / "Quill.svg")

    with tempfile.TemporaryDirectory(prefix="quill-icon-master-") as temp_dir:
        master_png = Path(temp_dir) / "master.png"
        render_svg_with_edge(svg_text, master_png)
        with Image.open(master_png) as raw:
            master = verify_master_rgba(raw)

        for size in PNG_SIZES:
            resized = master.resize((size, size), Image.Resampling.LANCZOS)
            out_png = output_dir / f"Quill-{size}.png"
            resized.save(out_png, format="PNG")
            print(f"Wrote {out_png} ({out_png.stat().st_size} bytes)")

        ico_path = output_dir / "Quill.ico"
        ico_master = master.resize((256, 256), Image.Resampling.LANCZOS)
        ico_master.save(ico_path, format="ICO", sizes=[(size, size) for size in ICO_SIZES])
        print(f"Wrote {ico_path} ({ico_path.stat().st_size} bytes) with sizes {ICO_SIZES}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate Quill app icons from SVG.")
    parser.add_argument("--input", required=True, help="Path to source SVG file.")
    parser.add_argument("--output", required=True, help="Output directory for generated assets.")
    args = parser.parse_args()

    build_icons(Path(args.input).resolve(), Path(args.output).resolve())
    return 0


if __name__ == "__main__":
    sys.exit(main())
