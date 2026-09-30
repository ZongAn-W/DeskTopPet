from pathlib import Path
import cv2
import numpy as np
from PIL import Image

SOURCE = Path("tools/reference.jpg")
OUT = Path("src/DesktopPet/Assets/Character")

image = cv2.imread(str(SOURCE), cv2.IMREAD_COLOR)
if image is None:
    raise SystemExit(f"Cannot read {SOURCE}")

h, w = image.shape[:2]
mask = np.zeros((h, w), np.uint8)
bgd = np.zeros((1, 65), np.float64)
fgd = np.zeros((1, 65), np.float64)
rect = (max(0, int(w * 0.34)), max(0, int(h * 0.05)), int(w * 0.65), int(h * 0.95))
cv2.grabCut(image, mask, rect, bgd, fgd, 8, cv2.GC_INIT_WITH_RECT)
alpha = np.where((mask == cv2.GC_FGD) | (mask == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
# Keep the connected foreground around the person and discard isolated city lights.
components, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > 0).astype(np.uint8), 8)
if components > 1:
    largest = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    alpha[labels != largest] = 0

ys, xs = np.where(alpha > 0)
if len(xs) == 0:
    raise SystemExit("Foreground extraction produced an empty mask")
pad = 12
x0, x1 = max(0, xs.min() - pad), min(w, xs.max() + pad + 1)
y0, y1 = max(0, ys.min() - pad), min(h, ys.max() + pad + 1)
rgba = cv2.cvtColor(image[y0:y1, x0:x1], cv2.COLOR_BGR2RGBA)
rgba[:, :, 3] = alpha[y0:y1, x0:x1]

subject = Image.fromarray(rgba)
subject.thumbnail((166, 202), Image.Resampling.LANCZOS)
canvas = Image.new("RGBA", (180, 210), (0, 0, 0, 0))
left = (180 - subject.width) // 2
top = 210 - subject.height - 2
canvas.alpha_composite(subject, (left, top))

OUT.mkdir(parents=True, exist_ok=True)
# NOTE: this writes one frame per state into that state's folder, using the same cropped
# photograph for all of them. It is a STARTING POINT only - every generated frame is the raw
# photo, not finished art, and the committed repository deliberately has no such frames. Replace
# each one before committing, and see src/DesktopPet/Assets/README.md for the layout rules.
states = {"idle": 1, "walk_left": 1, "walk_right": 1, "respond": 1, "sleep": 1, "drag": 1}
for state, count in states.items():
    folder = OUT / state
    folder.mkdir(parents=True, exist_ok=True)
    for index in range(count):
        frame = canvas
        if state == "walk_left":
            frame = canvas.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        frame.save(folder / f"{index:03d}.png")

print(f"Wrote {len(states)} photo-derived frames under {OUT} (one folder per state)")
