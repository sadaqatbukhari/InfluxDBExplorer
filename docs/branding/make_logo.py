"""Generates the Tickbird logo files from one shared geometry.

Usage (from the repository root):  python docs/branding/make_logo.py
Needs Pillow. Writes the SVG sources next to this script and the PNG/ICO files into the app assets.

The mark is a bird whose tail is a sparkline: the tail feathers are a time-series line with data points.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", "src", "Tickbird.Desktop", "Assets"))

BLUE = "#1A65A7"
NAVY = "#0F3D66"
SKY = "#BFE0FF"
AMBER = "#FFB400"
WHITE = "#FFFFFF"


# ---------------------------------------------------------------- geometry (512 x 512 design space)
def bezier(p0, p1, p2, p3, n=24):
    pts = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        pts.append((u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                    u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]))
    return pts


def ellipse(cx, cy, rx, ry, angle=0, n=72):
    a = math.radians(angle)
    out = []
    for i in range(n):
        t = 2 * math.pi * i / n
        x, y = rx * math.cos(t), ry * math.sin(t)
        out.append((cx + x * math.cos(a) - y * math.sin(a), cy + x * math.sin(a) + y * math.cos(a)))
    return out


def stroke(points, width):
    """A thick polyline as a polygon (round joins are added separately as circles)."""
    left, right = [], []
    for i, (x, y) in enumerate(points):
        if i == 0:
            dx, dy = points[1][0] - x, points[1][1] - y
        elif i == len(points) - 1:
            dx, dy = x - points[i - 1][0], y - points[i - 1][1]
        else:
            dx, dy = points[i + 1][0] - points[i - 1][0], points[i + 1][1] - points[i - 1][1]
        length = math.hypot(dx, dy) or 1
        nx, ny = -dy / length * width / 2, dx / length * width / 2
        left.append((x + nx, y + ny))
        right.append((x - nx, y - ny))
    return left + right[::-1]


def bird(body, wing, tail, eye_cut):
    """Shapes of the bird as (color, polygon) in drawing order."""
    shapes = []
    # Tail: a sparkline of data points sweeping up and to the left, with a dot at every vertex.
    line = [(176, 300), (138, 238), (106, 276), (66, 188)]
    shapes.append((tail, stroke(line, 18)))
    for x, y in line:
        shapes.append((tail, ellipse(x, y, 20, 20)))
    # Body, head and beak.
    shapes.append((body, ellipse(262, 284, 128, 84, -18)))
    shapes.append((body, ellipse(372, 206, 58, 58)))
    shapes.append((body, [(418, 188), (478, 214), (420, 238)]))
    # Wing.
    wing_pts = (bezier((196, 270), (232, 190), (332, 196), (366, 262))
                + bezier((366, 262), (322, 246), (254, 258), (196, 270)))
    shapes.append((wing, wing_pts))
    # Eye.
    shapes.append((eye_cut, ellipse(384, 198, 11, 11)))
    return shapes


def bbox(shapes):
    xs = [x for _, pts in shapes for x, _ in pts]
    ys = [y for _, pts in shapes for _, y in pts]
    return min(xs), min(ys), max(xs), max(ys)


def fit(shapes, box):
    """Scales and centers the shapes inside box=(x0,y0,x1,y1)."""
    bx0, by0, bx1, by1 = bbox(shapes)
    k = min((box[2] - box[0]) / (bx1 - bx0), (box[3] - box[1]) / (by1 - by0))
    ox = box[0] + ((box[2] - box[0]) - (bx1 - bx0) * k) / 2 - bx0 * k
    oy = box[1] + ((box[3] - box[1]) - (by1 - by0) * k) / 2 - by0 * k
    return [(c, [(x * k + ox, y * k + oy) for x, y in pts]) for c, pts in shapes]


def rounded_square(size, radius):
    pts = []
    for cx, cy, a0 in ((radius, radius, 180), (size - radius, radius, 270), (size - radius, size - radius, 0), (radius, size - radius, 90)):
        for i in range(0, 91, 6):
            a = math.radians(a0 + i)
            pts.append((cx + radius * math.cos(a), cy + radius * math.sin(a)))
    return pts


# ---------------------------------------------------------------- output helpers
def svg_polygon(color, pts, dx=0, dy=0, scale=1):
    data = " ".join(f"{(x * scale + dx):.1f},{(y * scale + dy):.1f}" for x, y in pts)
    return f'<polygon fill="{color}" points="{data}"/>'


def icon_shapes():
    return [(BLUE, rounded_square(512, 112))] + fit(bird(WHITE, SKY, AMBER, BLUE), (64, 80, 448, 432))


def mark_shapes():
    return bird(BLUE, SKY, AMBER, WHITE)


def raster(shapes, size, supersample=4, bounds=None, canvas=None):
    """Draws shapes on a transparent canvas. bounds=(x0,y0,x1,y1) in design space is fitted into the canvas."""
    x0, y0, x1, y1 = bounds or (0, 0, 512, 512)
    width, height = canvas or (size, size)
    scale = min(width / (x1 - x0), height / (y1 - y0))
    ox, oy = (width - (x1 - x0) * scale) / 2, (height - (y1 - y0) * scale) / 2
    big = Image.new("RGBA", (width * supersample, height * supersample), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    for color, pts in shapes:
        draw.polygon([((x - x0) * scale * supersample + ox * supersample,
                       (y - y0) * scale * supersample + oy * supersample) for x, y in pts], fill=color)
    return big.resize((width, height), Image.LANCZOS)


def wordmark(height=271, width=1571):
    shapes = mark_shapes()
    x0, y0, x1, y1 = bbox(shapes)
    mark = raster(shapes, height, bounds=(x0, y0, x1, y1), canvas=(int(height * (x1 - x0) / (y1 - y0)), height))
    out = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    out.alpha_composite(mark, (0, 0))
    font_path = next((p for p in (r"C:\Windows\Fonts\segoeuib.ttf", r"C:\Windows\Fonts\arialbd.ttf") if os.path.exists(p)), None)
    font = ImageFont.truetype(font_path, int(height * 0.74)) if font_path else ImageFont.load_default()
    draw = ImageDraw.Draw(out)
    x = mark.width + int(height * 0.12)
    for text, color in (("Tick", NAVY), ("bird", BLUE)):
        box = draw.textbbox((0, 0), text, font=font)
        y = (height - (box[3] - box[1])) // 2 - box[1]
        draw.text((x, y), text, font=font, fill=color)
        x += draw.textlength(text, font=font)
    return out.crop((0, 0, int(x) + 4, height))


def main():
    # SVG sources.
    with open(os.path.join(HERE, "tickbird-icon.svg"), "w", encoding="utf-8") as f:
        f.write('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">\n  '
                + "\n  ".join(svg_polygon(c, p) for c, p in icon_shapes()) + "\n</svg>\n")
    with open(os.path.join(HERE, "tickbird-mark.svg"), "w", encoding="utf-8") as f:
        x0, y0, x1, y1 = bbox(mark_shapes())
        f.write(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x0:.0f} {y0:.0f} {x1 - x0:.0f} {y1 - y0:.0f}">\n  '
                + "\n  ".join(svg_polygon(c, p) for c, p in mark_shapes()) + "\n</svg>\n")

    # App icon (PNG master + multi-size ICO).
    master = raster(icon_shapes(), 1254)
    master.save(os.path.join(ASSETS, "AppIcon.png"))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    master.resize((256, 256), Image.LANCZOS).save(os.path.join(ASSETS, "AppIcon.ico"), sizes=[(s, s) for s in sizes])

    # macOS icon, used by installer/package.py when it builds Tickbird.app.
    mac = os.path.normpath(os.path.join(HERE, "..", "..", "installer", "macos"))
    os.makedirs(mac, exist_ok=True)
    master.resize((1024, 1024), Image.LANCZOS).save(os.path.join(mac, "AppIcon.icns"), format="ICNS")

    # Installer logo and header wordmark.
    raster(icon_shapes(), 256).save(os.path.join(ASSETS, "tickbird-logo.png"))
    wordmark().save(os.path.join(ASSETS, "BrandLogo.png"))
    print("Logo files written to", ASSETS)


if __name__ == "__main__":
    main()
