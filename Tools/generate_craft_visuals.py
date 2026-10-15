"""Generate the hand-drawn craft goods/status textures used by the Unity presentation.

Run from the repository root:
    python tools/generate_craft_visuals.py

The output is deliberately simple, opaque-free, 64x64 PNG art so the textures can be
replaced later by keeping the same file names.
"""

from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GOODS = ROOT / "UnityProject/Assets/RTS/UI/Resources/Icons/Goods"
STATUS = ROOT / "UnityProject/Assets/RTS/UI/Resources/Icons/Status"
BELT = ROOT / "UnityProject/Assets/RTS/UI/Resources/Textures"

GOODS_COLORS = {
    "Food": (235, 193, 72), "Wood": (147, 91, 45), "Ore": (111, 91, 82),
    "Metal": (178, 190, 207), "Stone": (144, 148, 155), "Gems": (78, 202, 205),
    "Gold": (235, 179, 42), "Charcoal": (55, 54, 60), "Steel": (113, 143, 178),
    "BowGear": (156, 91, 47), "Rations": (204, 133, 55), "Bricks": (175, 70, 46),
    "Tack": (113, 67, 48), "Planks": (184, 124, 57), "Books": (92, 67, 128),
    "Preserves": (220, 107, 83), "TradeGoods": (67, 143, 104), "Incense": (161, 92, 177),
    "Charms": (218, 111, 179), "RepairKits": (74, 106, 128), "Tools": (80, 133, 142),
}


def icon_base(color):
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((3, 3, 60, 60), fill=(28, 35, 48, 235), outline=(235, 221, 177, 255), width=2)
    draw.ellipse((8, 8, 55, 55), fill=(*color, 255))
    return image, draw


def draw_goods(name, color):
    image, draw = icon_base(color)
    dark = tuple(max(0, c - 45) for c in color)
    light = tuple(min(255, c + 45) for c in color)
    if name == "Wood":
        draw.rounded_rectangle((15, 22, 50, 39), radius=7, fill=dark, outline=light, width=2)
        draw.ellipse((39, 22, 53, 39), fill=light, outline=dark, width=2)
        draw.line((20, 27, 41, 34), fill=light, width=3)
    elif name in ("Metal", "Steel", "Gold"):
        draw.polygon((13, 25, 20, 18, 49, 18, 55, 25, 49, 43, 20, 43), fill=dark, outline=light)
        draw.line((19, 24, 49, 24), fill=light, width=3)
        draw.line((25, 30, 43, 30), fill=light, width=2)
    elif name in ("Ore", "Stone", "Charcoal", "Gems"):
        points = [(16, 39), (12, 26), (24, 16), (41, 18), (52, 30), (44, 45), (25, 48)]
        draw.polygon(points, fill=dark, outline=light)
        draw.line((23, 23, 32, 39), fill=light, width=2)
        if name == "Gems":
            draw.polygon((28, 20, 40, 25, 35, 39, 23, 32), fill=(118, 246, 237), outline=(232, 255, 252))
    elif name in ("Food", "Rations", "Preserves"):
        draw.rounded_rectangle((17, 18, 47, 47), radius=6, fill=dark, outline=light, width=2)
        draw.line((18, 27, 46, 27), fill=light, width=3)
        draw.arc((24, 10, 40, 27), 180, 360, fill=light, width=2)
    elif name in ("BowGear", "Tack"):
        draw.arc((13, 14, 50, 50), 285, 75, fill=light, width=4)
        draw.line((16, 47, 48, 17), fill=dark, width=3)
        draw.line((22, 36, 41, 36), fill=light, width=2)
    elif name in ("Bricks", "Planks", "Books", "TradeGoods"):
        draw.rounded_rectangle((14, 18, 50, 45), radius=3, fill=dark, outline=light, width=2)
        draw.line((17, 27, 47, 27), fill=light, width=2)
        draw.line((17, 36, 47, 36), fill=light, width=2)
        if name == "Books":
            draw.line((31, 19, 31, 44), fill=light, width=2)
    elif name in ("Incense", "Charms"):
        draw.ellipse((18, 26, 46, 48), fill=dark, outline=light, width=2)
        draw.arc((20, 9, 38, 32), 180, 350, fill=light, width=3)
        draw.arc((31, 6, 49, 28), 180, 350, fill=light, width=3)
    elif name == "RepairKits":
        draw.rounded_rectangle((16, 24, 48, 42), radius=3, fill=dark, outline=light, width=2)
        draw.line((23, 20, 41, 46), fill=light, width=4)
        draw.line((41, 20, 23, 46), fill=light, width=4)
    elif name == "Tools":
        draw.line((18, 43, 43, 18), fill=light, width=6)
        draw.ellipse((13, 36, 27, 50), fill=dark, outline=light, width=2)
        draw.rectangle((38, 14, 48, 26), fill=dark, outline=light, width=2)
    else:
        draw.polygon((32, 13, 38, 27, 52, 29, 42, 39, 45, 52, 32, 45, 19, 52, 22, 39, 12, 29, 26, 27), fill=dark, outline=light)
    return image


def draw_status(name):
    colors = {
        "WaitingForMaterial": (208, 151, 48),
        "OutputBlocked": (190, 66, 54),
        "StockFull": (102, 81, 164),
        "Working": (55, 158, 106),
    }
    image, draw = icon_base(colors[name])
    white = (250, 246, 226, 255)
    if name == "WaitingForMaterial":
        draw.line((32, 16, 32, 42), fill=white, width=5)
        draw.polygon((22, 35, 42, 35, 32, 48), fill=white)
    elif name == "OutputBlocked":
        draw.line((20, 20, 44, 44), fill=white, width=5)
        draw.line((44, 20, 20, 44), fill=white, width=5)
    elif name == "StockFull":
        draw.rectangle((17, 28, 47, 45), fill=white)
        draw.line((17, 28, 32, 16, 47, 28), fill=white, width=4)
        draw.line((24, 33, 40, 33), fill=colors[name], width=3)
    else:
        draw.arc((15, 15, 49, 49), 35, 320, fill=white, width=5)
        draw.polygon((42, 13, 49, 14, 45, 23), fill=white)
    return image


def draw_belt():
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.polygon((4, 12, 22, 12, 38, 28, 54, 12, 60, 12, 38, 36, 60, 52, 54, 52, 38, 36, 22, 52, 4, 52, 26, 28), fill=(244, 218, 112, 255))
    draw.polygon((12, 17, 20, 17, 38, 35, 55, 17, 55, 25, 38, 43, 12, 25), fill=(86, 62, 38, 255))
    return image


def main():
    GOODS.mkdir(parents=True, exist_ok=True)
    STATUS.mkdir(parents=True, exist_ok=True)
    BELT.mkdir(parents=True, exist_ok=True)
    for name, color in GOODS_COLORS.items():
        draw_goods(name, color).save(GOODS / f"{name}.png")
    for name in ("WaitingForMaterial", "OutputBlocked", "StockFull", "Working"):
        draw_status(name).save(STATUS / f"{name}.png")
    draw_belt().save(BELT / "BeltChevron.png")
    print(f"generated {len(GOODS_COLORS)} goods, 4 status, 1 belt texture")


if __name__ == "__main__":
    main()
