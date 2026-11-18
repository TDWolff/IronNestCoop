# Generates packaging/icon.png (256x256, Thunderstore's required size) with no third-party packages.
# Design: dark steel background, orange gear ring (the Iron Nest), two small "crew" dots in the middle.
#   python -I tools/make_icon.py packaging/icon.png
import math, struct, sys, zlib

W = H = 256
BG = (28, 32, 38)
RING = (236, 140, 30)
CREW = [(235, 140, 30), (51, 153, 255)]

def pixel(x, y):
    cx, cy = x - 127.5, y - 127.5
    r = math.hypot(cx, cy)
    a = math.atan2(cy, cx)
    teeth = 12
    tooth = (math.cos(a * teeth) > 0.35)
    outer = 108 if tooth else 96
    if 70 <= r <= outer:
        return RING
    for i, col in enumerate(CREW):
        dx = cx - (-22 if i == 0 else 22)
        if math.hypot(dx, cy + 6) <= 16: return col          # head
        if abs(dx) <= 18 and 14 <= cy <= 42: return col       # body
    return BG

def png(path):
    raw = bytearray()
    for y in range(H):
        raw.append(0)
        for x in range(W):
            raw.extend(pixel(x, y))
    def chunk(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xFFFFFFFF)
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', W, H, 8, 2, 0, 0, 0)) \
        + chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b'')
    open(path, 'wb').write(data)

png(sys.argv[1] if len(sys.argv) > 1 else 'icon.png')
print('wrote', sys.argv[1] if len(sys.argv) > 1 else 'icon.png')
