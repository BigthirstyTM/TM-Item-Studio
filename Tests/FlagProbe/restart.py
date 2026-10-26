"""Restart the game after discarding ONLY a verified disposable Unnamed map.

Archives this investigation's logged native screenshots to avoid counter reuse.
The game may still be starting when this command returns; poll the bridge.
"""
import json
from pathlib import Path
import shutil
import subprocess
from game import call, destination

mode = call("GetMode")["mode"]
if mode == "MapEditor":
    assert call("GetMapInfo")["name"] == "Unnamed", "Refuse to discard a named map"
    response = call("BackToMainMenu")
    if response.get("blockedByDialog"):
        call("RespondDialog", {"action": "no"})
assert call("GetMode")["mode"] == "Menu"
number = 1
while (destination / f"native-webp-session{number}").exists():
    number += 1
archive = destination / f"native-webp-session{number}"
archive.mkdir()
for line in (destination / "evidence/captures.jsonl").read_text().splitlines():
    source = Path(json.loads(line)["capture"]["fullName"])
    if source.exists():
        shutil.move(str(source), str(archive / source.name))
subprocess.run(["powershell", "-NoProfile", "-Command",
    "$flagProcess=Get-Process Trackmania; "
    "if (@($flagProcess).Count -ne 1) { throw 'Expected one game process' }; "
    "Stop-Process -Id $flagProcess.Id; "
    "Start-Process 'uplay://launch/5595/0' -WindowStyle Hidden"], check=True)
print("Restart requested; archived", archive)
