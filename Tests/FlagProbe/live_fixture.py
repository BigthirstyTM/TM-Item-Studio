"""Set up ONLY an empty disposable map; capture research flag candidates.

These candidates did NOT establish correct custom cloth rendering. The assets
are archived outside the live Items directory; see the runtime research report.

Requires the installed localhost bridge and final items in TMIS_CustomFlags.
Commands: setup | editor | playground. Every call is recorded by game.py.
"""
import json
from pathlib import Path
import shutil
import sys
import time

from game import call, destination

MODELS = ["Flag16m", r"TMIS_CustomFlags\TMIS_CustomFlag_Standalone.Item.Gbx",
          r"TMIS_CustomFlags\TMIS_CustomFlag_WideStandalone.Item.Gbx"]
EVIDENCE = destination / "evidence"


def shot(label):
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    before = call("GetMode")
    began = time.time()
    # Use a fresh native format counter. Existing JPG names can fool the bridge's
    # existence check before the asynchronous capture has actually been written.
    capture = call("TakeScreenshot", {"currentView": True, "hideOverlay": True, "format": "webp",
                   "forceRes": True, "width": 1920, "height": 1080})
    if not capture.get("detected"):
        raise RuntimeError(capture)
    source = Path(capture["fullName"])
    deadline = time.monotonic() + 2
    while source.stat().st_mtime < began - 0.1 and time.monotonic() < deadline:
        time.sleep(0.05)
    assert source.stat().st_mtime >= began - 0.1, "Reject stale native screenshot"
    target = EVIDENCE / f"{label}.webp"
    shutil.copyfile(source, target)
    with (EVIDENCE / "captures.jsonl").open("a", encoding="utf-8") as output:
        output.write(json.dumps({"label": label, "mode": before, "capture": capture,
                                 "copy": str(target)}) + "\n")
    print(target.name, flush=True)


def setup():
    assert call("GetMode")["mode"] == "MapEditor"
    info = call("GetMapInfo")
    assert info["name"] == "Unnamed" and info["nbItems"] == 0, "Use an empty scratch map"
    call("tm-mcp-pack-epp.SetAgentTag", {"tag": "tmis:flag-standalone-20261021"})
    for z, base, yaw in [(512, 512, 1.57079632679), (640, 512, 0), (1360, 1360, 1.57079632679)]:
        for n, model in enumerate(MODELS):
            picker = call("tm-mcp-pack-epp.SelectItemModel", {"itemPath": model})
            assert picker["selected"]["ok"]
            placed = call("tm-mcp-pack-epp.PlaceItem", {"itemPath": model, "x": base + 12 * n,
                          "y": 8, "z": z, "yaw": yaw, "autofocus": False})
            assert placed["placed"] and placed["afterItems"] == placed["beforeItems"] + 1
    items = call("GetItems", {"limit": 30})
    assert items["total"] == 9
    print(json.dumps(items), flush=True)
    for name, z in [("RoadTechStart", 14), ("RoadTechStraight", 15), ("RoadTechStraight", 16),
                    ("RoadTechStraight", 17), ("RoadTechFinish", 18)]:
        assert call("tm-mcp-pack-epp.PlaceGridBlock", {"blockName": name, "coord": [10, 9, z],
                    "dir": "North", "onGround": True, "autofocus": False})["placed"]
    print(call("SaveMapAs", {"name": "TMIS_CustomFlags_Standalone"}), flush=True)


def matrix(stage):
    assert call("GetItems", {"limit": 30})["total"] == 9
    call("ControlOverlay", {"action": "hide"})
    if stage == "editor":
        call("ControlCamera", {"action": "look", "direction": "North", "smooth": False})
        call("ControlCamera", {"action": "setVStep", "step": "Low", "smooth": False})
        time.sleep(0.5)
    for distance in [35, 60, 70, 120, 140, 350, 500, 525, 550]:
        if stage == "editor":
            call("SetEditorCamera", {"x": 1372, "y": 19, "z": 1360, "distance": distance})
        else:
            call("ControlPlayCamera", {"action": "set_position", "x": 1372, "y": 24,
                 "z": 1360 - distance, "target": {"x": 1372, "y": 19, "z": 1360},
                 "fov": 75, "settleFrames": 10})
        shot(f"{stage}-far-{distance}m")
    if stage != "editor":
        for x, label in [(1360, "vanilla"), (1372, "parity"), (1384, "wide")]:
            call("ControlPlayCamera", {"action": "set_position", "x": x, "y": 20,
                 "z": 1348, "target": {"x": x, "y": 19, "z": 1360},
                 "fov": 75, "settleFrames": 10})
            shot(f"{stage}-close-{label}-12m")
        call("ControlPlayCamera", {"action": "set_position", "x": 524, "y": 23, "z": 600,
             "target": {"x": 524, "y": 19, "z": 640}, "settleFrames": 10})
        shot(f"{stage}-rotation-zero")
        call("ControlPlayCamera", {"action": "set_position", "x": 1372, "y": 20, "z": 1336,
             "target": {"x": 1372, "y": 18, "z": 1360}, "fov": 55, "settleFrames": 10})
        shot(f"{stage}-comparison")


if __name__ == "__main__":
    if sys.argv[1] == "setup":
        setup()
    else:
        matrix(sys.argv[1])
