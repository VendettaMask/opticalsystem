#!/usr/bin/env python3
"""Generate deterministic desktop icon and splash assets."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "OptilandWorkbench.App" / "Assets" / "Brand"
SCALE = 4

CHARCOAL = "#121A22"
INK = "#20262D"
MUTED = "#66717C"
PAPER = "#F7F9FC"
BLUE = "#1687E8"
GREEN = "#00A878"
RED = "#F05252"
CYAN = "#BDEAF3"
WHITE = "#FFFFFF"


def pt(value: float) -> int:
    return round(value * SCALE)


def points(values: list[tuple[float, float]]) -> list[tuple[int, int]]:
    return [(pt(x), pt(y)) for x, y in values]


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        "/System/Library/Fonts/SFNS.ttf" if not bold else "/System/Library/Fonts/SFNS-Bold.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, pt(size))
        except OSError:
            continue
    return ImageFont.load_default()


def lens_polygon(cx: float, cy: float, height: float, thickness: float) -> list[tuple[float, float]]:
    samples = 80
    half_height = height / 2
    result: list[tuple[float, float]] = []
    for index in range(samples + 1):
        y = -half_height + (height * index / samples)
        normalized = y / half_height
        x = cx - thickness * (0.52 - 0.25 * normalized * normalized)
        result.append((x, cy + y))
    for index in range(samples, -1, -1):
        y = -half_height + (height * index / samples)
        normalized = y / half_height
        x = cx + thickness * (0.52 - 0.25 * normalized * normalized)
        result.append((x, cy + y))
    return result


def draw_dashed_axis(draw: ImageDraw.ImageDraw, start: float, end: float, y: float, width: float) -> None:
    x = start
    while x < end:
        draw.line(points([(x, y), (min(x + 22, end), y)]), fill="#73808C", width=pt(width))
        x += 36


def draw_mark(canvas: Image.Image, bounds: tuple[int, int, int, int], rounded: bool = True) -> None:
    x, y, width, height = bounds
    layer = Image.new("RGBA", (width * SCALE, height * SCALE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    radius = min(width, height) * 0.19
    box = (pt(0), pt(0), pt(width), pt(height))
    if rounded:
        draw.rounded_rectangle(box, radius=pt(radius), fill=CHARCOAL)
    else:
        draw.rectangle(box, fill=CHARCOAL)

    center_y = height / 2
    draw_dashed_axis(draw, width * 0.12, width * 0.88, center_y, max(2, width * 0.006))
    ray_width = max(5, pt(width * 0.018))
    ray_specs = [
        (height * 0.34, BLUE, height * 0.46),
        (height * 0.50, GREEN, height * 0.50),
        (height * 0.66, RED, height * 0.54),
    ]
    for input_y, color, lens_y in ray_specs:
        draw.line(
            points([
                (width * 0.14, input_y),
                (width * 0.45, lens_y),
                (width * 0.79, center_y),
                (width * 0.88, center_y),
            ]),
            fill=color,
            width=ray_width,
            joint="curve",
        )

    lens = points(lens_polygon(width * 0.48, center_y, height * 0.58, width * 0.20))
    draw.polygon(lens, fill=CYAN)
    draw.line(lens + [lens[0]], fill=WHITE, width=max(5, pt(width * 0.017)), joint="curve")

    focus_x = width * 0.79
    focus_y = center_y
    glow = width * 0.032
    draw.ellipse(
        (pt(focus_x - glow), pt(focus_y - glow), pt(focus_x + glow), pt(focus_y + glow)),
        fill=WHITE,
    )
    draw.line(points([(focus_x - glow * 1.8, focus_y), (focus_x + glow * 1.8, focus_y)]), fill=WHITE, width=pt(2))
    draw.line(points([(focus_x, focus_y - glow * 1.8), (focus_x, focus_y + glow * 1.8)]), fill=WHITE, width=pt(2))

    canvas.alpha_composite(layer.resize((width, height), Image.Resampling.LANCZOS), (x, y))


def create_icon() -> Image.Image:
    icon = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    draw_mark(icon, (0, 0, 1024, 1024))
    return icon


def create_splash(icon: Image.Image) -> Image.Image:
    splash = Image.new("RGB", (1280 * SCALE, 720 * SCALE), PAPER)
    draw = ImageDraw.Draw(splash)

    icon_small = icon.resize((176 * SCALE, 176 * SCALE), Image.Resampling.LANCZOS)
    splash.paste(icon_small, (pt(94), pt(86)), icon_small)

    draw.text((pt(318), pt(105)), "OPTICAL SYSTEM DESIGN", fill=INK, font=font(45, True))
    draw.text((pt(321), pt(171)), "Sequential optical design and analysis", fill=MUTED, font=font(20))
    draw.text((pt(321), pt(211)), "S.T.A.R. Labs", fill=BLUE, font=font(17, True))
    draw.line(points([(94, 302), (1186, 302)]), fill="#CAD2DA", width=pt(1))

    axis_y = 492
    draw_dashed_axis(draw, 92, 1188, axis_y, 1.2)
    lenses = [
        (455, 492, 242, 58),
        (616, 492, 196, 48),
        (790, 492, 226, 54),
    ]
    for cx, cy, height, thickness in lenses:
        polygon = points(lens_polygon(cx, cy, height, thickness))
        draw.polygon(polygon, fill="#DDF3F7")
        draw.line(polygon + [polygon[0]], fill="#43515C", width=pt(3), joint="curve")

    ray_specs = [
        (410, BLUE, 466, 492),
        (492, GREEN, 492, 492),
        (574, RED, 518, 492),
    ]
    for start_y, color, first_lens_y, focus_y in ray_specs:
        route = [(92, start_y), (455, first_lens_y), (616, axis_y + (first_lens_y - axis_y) * 0.55), (790, axis_y + (first_lens_y - axis_y) * 0.22), (1080, focus_y)]
        draw.line(points(route), fill=color, width=pt(3), joint="curve")

    draw.ellipse((pt(1072), pt(484), pt(1088), pt(500)), fill=INK)
    draw.text((pt(94), pt(652)), "Initializing optical workspace", fill=MUTED, font=font(15))
    draw.text((pt(1186), pt(652)), "1.0.0", fill=MUTED, font=font(15), anchor="ra")
    return splash.resize((1280, 720), Image.Resampling.LANCZOS)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    icon = create_icon()
    icon.save(OUTPUT / "AppIcon.png", optimize=True)
    icon.save(
        OUTPUT / "AppIcon.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    icon.save(OUTPUT / "AppIcon.icns", format="ICNS")
    create_splash(icon).save(OUTPUT / "Splash.png", optimize=True)
    print(f"Generated brand assets in {OUTPUT}")


if __name__ == "__main__":
    main()
