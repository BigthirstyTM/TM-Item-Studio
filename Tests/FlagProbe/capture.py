"""Record real native screenshots, retaining source paths and capture times.

Run only in the prepared flag playground camera; this does not generate motion.
The labels correspond to the far fixture at x=1360/1372/1384, z=1360.
"""
import datetime
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

from PIL import Image, ImageDraw, ImageFont
from game import call, destination


def decorate(raw, frame, mode, elapsed):
    """Labels identify inputs only; changed rendered geometry is NOT proven."""
    font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 25)
    small = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 20)
    picture = Image.open(raw).convert("RGB")
    draw = ImageDraw.Draw(picture)
    draw.rectangle((0, 0, 1920, 118), fill=(17, 22, 28))
    for x, label in [(440, "Custom - gewijzigde vertices (proef)"),
                     (960, "Custom - vanilla geometrie"), (1480, "Vanilla Flag16m")]:
        draw.text((x, 24), label, font=font, anchor="mm", fill="white")
    draw.text((960, 63), "Onderzoeksbeelden: correcte eigen doekvorm NIET bewezen",
              font=small, anchor="mm", fill=(255, 196, 103))
    draw.text((960, 96), f"Echte gamebeelden | {mode} | t = {elapsed:.2f} s",
              font=small, anchor="mm", fill=(191, 205, 217))
    picture.save(frame, quality=93)


def encode(folder):
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "concat", "-safe", "0",
                    "-i", str(folder / "frames.ffconcat"), "-r", "8", "-c:v", "libx264",
                    "-crf", "19", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
                    str(destination / "custom-flags-ingame.mp4")], check=True)


def relabel():
    folder = destination / "motion"
    mode = json.loads((folder / "scene.json").read_text())["mode"]
    records = json.loads((folder / "frames.json").read_text())
    for record in records:
        decorate(folder / record["raw"], folder / f"frame-{record['index']:03}.jpg", mode, record["elapsed"])
    encode(folder)
    print("Relabeled original native frames; no new game capture or motion generated.")


def main():
    folder = destination / "motion"
    folder.mkdir(parents=True, exist_ok=True)
    records = []
    mode = call("GetMode")["mode"]
    (folder / "scene.json").write_text(json.dumps({"mode": mode,
       "items": call("GetItems", {"limit": 30}),
       "camera": call("ControlPlayCamera", {"action": "get"})}, indent=2), encoding="utf-8")
    start = time.monotonic()
    for index in range(81):  # 20 seconds, longer than the vanilla 8-16 s period.
        time.sleep(max(0, start + index / 4 - time.monotonic()))
        began = time.monotonic()
        wall_began = time.time()
        shot = call("TakeScreenshot", {"currentView": True, "hideOverlay": True, "format": "webp",
                    "forceRes": True, "width": 1920, "height": 1080})
        if not shot.get("detected"):
            raise RuntimeError(f"Capture {index} did not produce a file: {shot}")
        source = Path(shot["fullName"])
        deadline = time.monotonic() + 2
        while source.stat().st_mtime < wall_began - 0.1 and time.monotonic() < deadline:
            time.sleep(0.025)
        if source.stat().st_mtime < wall_began - 0.1:
            raise RuntimeError("Native capture returned an old file; reject stale evidence")
        raw = folder / f"raw-{index:03}.webp"
        shutil.copyfile(source, raw)
        elapsed = began - start
        # Looking north, +X projects LEFT in the game's rendered camera.
        decorate(raw, folder / f"frame-{index:03}.jpg", mode, elapsed)
        records.append({"index": index, "elapsed": elapsed,
                        "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                        "nativeSource": str(source), "raw": raw.name})
    (folder / "frames.json").write_text(json.dumps(records, indent=2), encoding="utf-8")
    concat = []
    for a, b in zip(records, records[1:]):
        concat.extend([f"file 'frame-{a['index']:03}.jpg'", f"duration {b['elapsed']-a['elapsed']:.6f}"])
    concat.append(f"file 'frame-{records[-1]['index']:03}.jpg'")
    (folder / "frames.ffconcat").write_text("\n".join(concat) + "\n", encoding="utf-8")
    encode(folder)
    print(json.dumps({"frames": len(records), "elapsed": records[-1]["elapsed"],
                      "video": str(destination / "custom-flags-ingame.mp4")}))


if __name__ == "__main__":
    relabel() if "--relabel" in sys.argv else main()
