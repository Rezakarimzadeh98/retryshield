"""Generate the small README flow animation without external design tooling."""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "assets" / "retryshield-demo.gif"
WIDTH, HEIGHT = 960, 500

BG = "#070b18"
PANEL = "#11182a"
TEXT = "#f8fafc"
MUTED = "#94a3b8"
PURPLE = "#8b5cf6"
CYAN = "#22d3ee"
GREEN = "#34d399"
RED = "#fb7185"


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    name = "segoeuib.ttf" if bold else "segoeui.ttf"
    return ImageFont.truetype(str(Path("C:/Windows/Fonts") / name), size)


def rounded_box(draw: ImageDraw.ImageDraw, xy: tuple[int, int, int, int], label: str, color: str) -> None:
    draw.rounded_rectangle(xy, radius=16, fill=PANEL, outline=color, width=3)
    box = draw.textbbox((0, 0), label, font=font(21, True))
    x = (xy[0] + xy[2] - (box[2] - box[0])) // 2
    y = (xy[1] + xy[3] - (box[3] - box[1])) // 2 - 2
    draw.text((x, y), label, fill=TEXT, font=font(21, True))


def arrow(draw: ImageDraw.ImageDraw, start: tuple[int, int], end: tuple[int, int], color: str, progress: float) -> None:
    x = int(start[0] + (end[0] - start[0]) * progress)
    y = int(start[1] + (end[1] - start[1]) * progress)
    draw.line((start[0], start[1], x, y), fill=color, width=5)
    if progress >= 0.98:
        draw.polygon([(x, y), (x - 14, y - 9), (x - 14, y + 9)], fill=color)


def frame(step: int, phase: float) -> Image.Image:
    image = Image.new("RGB", (WIDTH, HEIGHT), BG)
    draw = ImageDraw.Draw(image)
    draw.text((42, 30), "RetryShield", fill=TEXT, font=font(34, True))
    draw.text((42, 76), "One side effect. Safe retries.", fill=MUTED, font=font(20))

    client = (42, 190, 205, 290)
    gateway = (310, 170, 555, 310)
    upstream = (660, 190, 910, 290)
    database = (350, 375, 515, 455)
    rounded_box(draw, client, "Client", CYAN)
    rounded_box(draw, gateway, "Idempotency Gateway", PURPLE)
    rounded_box(draw, upstream, "Payment API", GREEN)
    rounded_box(draw, database, "PostgreSQL", "#60a5fa")

    messages = [
        ("Request #1", "claim key before forwarding", CYAN),
        ("Atomic claim", "key is now processing", PURPLE),
        ("Forward once", "upstream commits payment", GREEN),
        ("Store response", "status, headers and body", "#60a5fa"),
        ("Retry #2", "duplicate stops at the gateway", RED),
        ("Exact replay", "same response, no new charge", GREEN),
    ]
    title, subtitle, accent = messages[step]
    draw.text((42, 120), title, fill=accent, font=font(24, True))
    draw.text((190, 126), subtitle, fill=MUTED, font=font(17))

    if step == 0:
        arrow(draw, (205, 240), (310, 240), CYAN, phase)
    elif step == 1:
        arrow(draw, (432, 310), (432, 375), PURPLE, phase)
    elif step == 2:
        arrow(draw, (555, 240), (660, 240), GREEN, phase)
        draw.text((754, 310), "charge count: 1", fill=GREEN, font=font(18, True))
    elif step == 3:
        arrow(draw, (432, 310), (432, 375), "#60a5fa", phase)
        draw.text((350, 465), "201 Created saved", fill="#60a5fa", font=font(17, True))
    elif step == 4:
        arrow(draw, (205, 240), (310, 240), RED, phase)
        draw.line((580, 218, 610, 262), fill=RED, width=7)
        draw.line((610, 218, 580, 262), fill=RED, width=7)
        draw.text((648, 310), "not forwarded", fill=RED, font=font(18, True))
    else:
        arrow(draw, (310, 260), (205, 260), GREEN, phase)
        draw.text((75, 325), "201 Created · replayed", fill=GREEN, font=font(18, True))

    draw.text((662, 448), "Make API retries safe.", fill=TEXT, font=font(18, True))
    return image


def main() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    frames = []
    for step in range(6):
        for index in range(5):
            frames.append(frame(step, (index + 1) / 5))
        frames.extend([frame(step, 1.0)] * 4)
    frames[0].save(
        OUTPUT,
        save_all=True,
        append_images=frames[1:],
        duration=110,
        loop=0,
        optimize=True,
    )
    print(OUTPUT)


if __name__ == "__main__":
    main()
