"""Draws the app icon (src/Launcher/web/app.png, src/Launcher/app.ico). Run: python3 assets/make_icon.py"""
import math, os
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
S = 1024  # draw big, scale down

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def icon(size=S):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    # Rounded-square tile with a diagonal blue -> violet gradient (macOS-style "squircle-ish" radius).
    grad = Image.new("RGBA", (size, size))
    top, bottom = (10, 132, 255), (140, 72, 255)
    px = grad.load()
    for y in range(size):
        for x in range(size):
            t = (x * 0.35 + y * 0.65) / size
            px[x, y] = lerp(top, bottom, t) + (255,)
    mask = Image.new("L", (size, size), 0)
    m = int(size * 0.06)
    ImageDraw.Draw(mask).rounded_rectangle([m, m, size - m, size - m], radius=int(size * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    # Soft highlight on the top half.
    hl = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(hl).ellipse([-size * 0.2, -size * 0.75, size * 1.2, size * 0.45], fill=(255, 255, 255, 38))
    hl.putalpha(Image.composite(hl.getchannel("A"), Image.new("L", (size, size), 0), mask))
    img = Image.alpha_composite(img, hl)

    # White crosshair: ring + four gaps + centre dot.
    d = ImageDraw.Draw(img)
    c = size / 2
    r = size * 0.24
    w = int(size * 0.055)
    d.ellipse([c - r, c - r, c + r, c + r], outline=(255, 255, 255, 255), width=w)
    arm_in, arm_out = size * 0.10, size * 0.33
    for ang in (0, 90, 180, 270):
        dx, dy = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        d.line([c + dx * arm_in, c + dy * arm_in, c + dx * arm_out, c + dy * arm_out], fill=(255, 255, 255, 255), width=w)
    dot = size * 0.035
    d.ellipse([c - dot, c - dot, c + dot, c + dot], fill=(255, 255, 255, 255))
    return img

big = icon()
big.resize((512, 512), Image.LANCZOS).save(os.path.join(HERE, "..", "src", "Launcher", "web", "app.png"))
sizes = [16, 24, 32, 48, 64, 128, 256]
big.save(os.path.join(HERE, "..", "src", "Launcher", "app.ico"), sizes=[(s, s) for s in sizes])
print("ok")

# Installer welcome/finish side image (NSIS wants 164x314 BMP): gradient with the logo.
W, H = 164, 314
side = Image.new("RGB", (W, H))
sp = side.load()
for y in range(H):
    for x in range(W):
        sp[x, y] = lerp((10, 132, 255), (110, 60, 230), (y * 0.8 + x * 0.2) / H)
logo = icon(512).resize((96, 96), Image.LANCZOS)
side.paste(logo, ((W - 96) // 2, 40), logo)
side.save(os.path.join(HERE, "..", "installer", "welcome.bmp"))
print("installer image ok")
