"""Composite a raw Quill window capture onto a 16:10 backdrop with rounded corners and soft shadows."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


def parse_insets(value: str) -> tuple[int, int, int, int]:
    """Parse comma-separated left,top,right,bottom physical pixel crop insets."""
    parts = [int(part.strip()) for part in value.split(",")]
    if len(parts) != 4 or any(part < 0 for part in parts):
        raise ValueError(f"Expected 4 non-negative integers for --crop-insets, got: {value!r}")
    return parts[0], parts[1], parts[2], parts[3]


def build_fallback_backdrop(width: int, height: int, theme: str) -> Image.Image:
    """Create a smooth Fluent-inspired studio gradient backdrop when no image is supplied."""
    # Render a small mesh grid and bicubic-upscale it for smooth gradient transitions.
    grid_w, grid_h = 160, 100
    base = Image.new("RGB", (grid_w, grid_h))
    pixels = base.load()
    if pixels is None:
        raise RuntimeError("Failed to allocate backdrop pixel buffer.")

    for y in range(grid_h):
        ny = y / max(1, grid_h - 1)
        for x in range(grid_w):
            nx = x / max(1, grid_w - 1)
            diag = 0.55 * nx + 0.45 * ny
            glow = max(0.0, 1.0 - ((nx - 0.72) ** 2 + (ny - 0.28) ** 2) * 2.1)
            if theme == "dark":
                # Warm golden-amber glow paired with muted slate-blue edges.
                r = int(58 + 78 * diag + 88 * glow)
                g = int(74 + 62 * diag + 66 * glow)
                b = int(102 + 28 * (1.0 - nx) + 22 * glow)
            else:
                # Cool slate-blue and frosted periwinkle-silver studio tones.
                r = int(112 + 48 * nx + 38 * glow)
                g = int(136 + 44 * diag + 32 * glow)
                b = int(178 + 38 * (1.0 - ny) + 20 * glow)
            pixels[x, y] = (min(255, r), min(255, g), min(255, b))

    return base.resize((width, height), Image.Resampling.BICUBIC).convert("RGBA")


def load_backdrop(path: Path | None, width: int, height: int, theme: str) -> Image.Image:
    """Load, cover-fit, and center-crop the backdrop image to the target canvas dimensions."""
    if path is None:
        return build_fallback_backdrop(width, height, theme)
    if not path.is_file():
        raise FileNotFoundError(f"Backdrop image not found: {path}")

    with Image.open(path) as source:
        rgba = source.convert("RGBA")

    src_w, src_h = rgba.size
    scale = max(width / src_w, height / src_h)
    scaled_w = max(width, round(src_w * scale))
    scaled_h = max(height, round(src_h * scale))
    resized = rgba.resize((scaled_w, scaled_h), Image.Resampling.LANCZOS)

    left = (scaled_w - width) // 2
    top = (scaled_h - height) // 2
    return resized.crop((left, top, left + width, top + height))


def crop_window(image: Image.Image, insets: tuple[int, int, int, int]) -> Image.Image:
    """Trim DWM invisible resize border insets from the captured window screenshot."""
    left, top, right, bottom = insets
    width, height = image.size
    if left + right >= width or top + bottom >= height:
        raise ValueError(f"Crop insets {insets} exceed image dimensions {image.size}.")
    if any(insets):
        return image.crop((left, top, width - right, height - bottom))
    return image


def fit_window(image: Image.Image, max_width: int, max_height: int) -> Image.Image:
    """Resize the window screenshot cleanly to fit within the padded canvas bounds."""
    src_w, src_h = image.size
    scale = min(1.0, max_width / src_w, max_height / src_h)
    target_w = max(1, round(src_w * scale))
    target_h = max(1, round(src_h * scale))
    if (target_w, target_h) == (src_w, src_h):
        return image.copy()
    return image.resize((target_w, target_h), Image.Resampling.LANCZOS)


def create_rounded_mask(width: int, height: int, radius: int, supersample: int = 4) -> Image.Image:
    """Build an antialiased rounded-rectangle alpha mask via supersampled rasterization."""
    mask = Image.new("L", (width * supersample, height * supersample), 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle(
        (0, 0, width * supersample - 1, height * supersample - 1),
        radius=radius * supersample,
        fill=255,
    )
    return mask.resize((width, height), Image.Resampling.LANCZOS)


def apply_rounded_corners(image: Image.Image, radius: int) -> Image.Image:
    """Apply a crisp supersampled rounded-rectangle alpha mask to the window image."""
    if radius <= 0:
        return image.convert("RGBA")
    rgba = image.convert("RGBA")
    mask = create_rounded_mask(rgba.width, rgba.height, radius)
    alpha = rgba.getchannel("A")
    # Preserve existing alpha where present while clipping outside the rounded frame.
    combined = Image.composite(alpha, Image.new("L", rgba.size, 0), mask)
    rgba.putalpha(combined)
    return rgba


def add_drop_shadow(
    canvas: Image.Image,
    win_x: int,
    win_y: int,
    win_w: int,
    win_h: int,
    radius: int,
    theme: str,
) -> Image.Image:
    """Composite a wide ambient shadow and tight contact shadow behind the window."""
    ambient_alpha = 88 if theme == "dark" else 56
    contact_alpha = 62 if theme == "dark" else 36

    layers = [
        (0, 22, 48, ambient_alpha),
        (0, 6, 14, contact_alpha),
    ]

    result = canvas
    mask = create_rounded_mask(win_w, win_h, radius)
    for dx, dy, blur_radius, opacity in layers:
        layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        shadow_alpha = mask.point(lambda p: round(p * opacity / 255))
        shadow_fill = Image.new("RGBA", (win_w, win_h), (8, 12, 20, 255))
        shadow_fill.putalpha(shadow_alpha)
        layer.paste(shadow_fill, (win_x + dx, win_y + dy))
        blurred = layer.filter(ImageFilter.GaussianBlur(radius=blur_radius))
        result = Image.alpha_composite(result, blurred)

    return result


def draw_rim_stroke(
    canvas: Image.Image,
    win_x: int,
    win_y: int,
    win_w: int,
    win_h: int,
    radius: int,
    theme: str,
) -> Image.Image:
    """Draw a subtle 1px antialiased rounded border along the outer window edge."""
    supersample = 4
    stroke_color = (255, 255, 255, 40) if theme == "dark" else (15, 23, 42, 28)
    overlay_small = Image.new("RGBA", (win_w * supersample, win_h * supersample), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay_small)
    draw.rounded_rectangle(
        (0, 0, win_w * supersample - 1, win_h * supersample - 1),
        radius=radius * supersample,
        outline=stroke_color,
        width=supersample,
    )
    rim = overlay_small.resize((win_w, win_h), Image.Resampling.LANCZOS)

    full_overlay = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    full_overlay.paste(rim, (win_x, win_y))
    return Image.alpha_composite(canvas, full_overlay)


def compose_promo(
    input_path: Path,
    output_path: Path,
    background_path: Path | None,
    theme: str,
    crop_insets: tuple[int, int, int, int],
    canvas_width: int,
    canvas_height: int,
    padding_x: float,
    padding_y: float,
    corner_radius: int,
) -> None:
    """Compose the final promotional screenshot and write it as an optimized PNG."""
    if not input_path.is_file():
        raise FileNotFoundError(f"Input screenshot not found: {input_path}")

    with Image.open(input_path) as raw:
        cropped = crop_window(raw.convert("RGBA"), crop_insets)

    extrema = cropped.convert("RGB").getextrema()
    if all(lo == hi for lo, hi in extrema):
        raise RuntimeError(
            f"Input screenshot '{input_path}' is a solid-color frame ({extrema}). "
            "Ensure the capture script runs on an active, unlocked interactive desktop."
        )

    max_win_w = max(1, round(canvas_width * (1.0 - 2.0 * padding_x)))
    max_win_h = max(1, round(canvas_height * (1.0 - 2.0 * padding_y)))
    fitted = fit_window(cropped, max_win_w, max_win_h)
    rounded = apply_rounded_corners(fitted, corner_radius)

    win_w, win_h = rounded.size
    win_x = (canvas_width - win_w) // 2
    # Slightly lift the window vertically so the bottom drop shadow stays optically centered.
    win_y = max(0, (canvas_height - win_h) // 2 - 4)

    canvas = load_backdrop(background_path, canvas_width, canvas_height, theme)
    canvas = add_drop_shadow(canvas, win_x, win_y, win_w, win_h, corner_radius, theme)

    window_layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    window_layer.paste(rounded, (win_x, win_y))
    canvas = Image.alpha_composite(canvas, window_layer)
    canvas = draw_rim_stroke(canvas, win_x, win_y, win_w, win_h, corner_radius, theme)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(output_path, format="PNG", optimize=True, compress_level=9)
    size_kb = output_path.stat().st_size / 1024.0
    print(f"Wrote {output_path} ({canvas_width}x{canvas_height}, window={win_w}x{win_h}, {size_kb:.1f} KB)")


def main() -> int:
    parser = argparse.ArgumentParser(description="Composite a Quill window screenshot into a README promo graphic.")
    parser.add_argument("--input", required=True, help="Path to raw window screenshot PNG.")
    parser.add_argument("--output", required=True, help="Path to output promotional PNG.")
    parser.add_argument("--background", default=None, help="Optional backdrop wallpaper PNG.")
    parser.add_argument("--theme", choices=["dark", "light"], default="dark", help="Window theme for shadow/rim tuning.")
    parser.add_argument("--crop-insets", default="0,0,0,0", help="Physical pixel crop insets: left,top,right,bottom.")
    parser.add_argument("--canvas-width", type=int, default=1920, help="Output canvas width in pixels.")
    parser.add_argument("--canvas-height", type=int, default=1200, help="Output canvas height in pixels.")
    parser.add_argument("--padding-x", type=float, default=0.075, help="Horizontal outer margin fraction per side.")
    parser.add_argument("--padding-y", type=float, default=0.08, help="Vertical outer margin fraction per side.")
    parser.add_argument("--corner-radius", type=int, default=12, help="Window corner radius in output pixels.")
    args = parser.parse_args()

    compose_promo(
        input_path=Path(args.input).resolve(),
        output_path=Path(args.output).resolve(),
        background_path=Path(args.background).resolve() if args.background else None,
        theme=args.theme,
        crop_insets=parse_insets(args.crop_insets),
        canvas_width=args.canvas_width,
        canvas_height=args.canvas_height,
        padding_x=args.padding_x,
        padding_y=args.padding_y,
        corner_radius=args.corner_radius,
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
