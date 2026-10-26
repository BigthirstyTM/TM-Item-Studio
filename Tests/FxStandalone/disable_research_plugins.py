"""Disable only the four known leaf FX test plugins for cold playback proof."""
import importlib.util
import json
from pathlib import Path
import time

repo = Path(__file__).resolve().parents[2]
home = Path.home()
helper = home / "OpenplanetPlugins/tm-control-mcp/tools/call.py"
spec = importlib.util.spec_from_file_location("tm_bridge", helper)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)

def call(tool, data=None):
    response = bridge.send_request("127.0.0.1", 30006, 20,
                                   {"route": "call", "tool": tool, "input": data or {}})
    assert response["ok"] and response["data"]["result"]["success"], response
    return response["data"]["result"]["output"]

ids = ["TMISFxVanillaCopy", "TMISFxEmbeddedPlacement", "TMISFxNativeProbe", "TMISFxCustomPlacement"]
plugins = call("ListPlugins")["plugins"]
assert not [p["id"] for p in plugins if any(x in ids for x in p.get("dependencies", []))], "Research plugin has dependents"
log = home / "OpenplanetNext/Openplanet.log"
boundary = log.stat().st_size
responses = [call("ControlPlugin", {"action": "disable", "id": plugin_id}) for plugin_id in ids]
time.sleep(.5)
after = call("ListPlugins")["plugins"]
research = [p for p in after if p["id"] in ids]
assert all(not p["enabled"] for p in research), research
evidence = {"ids": ids, "before": [p for p in plugins if p["id"] in ids],
            "reverseDependents": [], "responses": responses, "after": research,
            "logOffset": boundary, "freshLog": log.read_bytes()[boundary:].decode("utf-8", errors="replace"),
            "mode": call("GetMode"), "nativeItemMutation": False}
output = repo / "artifacts/fx-clones/fx-cold-packaged-proof-20261025/research-plugins-disabled.json"
output.parent.mkdir(parents=True, exist_ok=True)
assert not output.exists(), "Do not overwrite lifecycle evidence"
output.write_text(json.dumps(evidence, indent=2), encoding="utf-8")
print(json.dumps({"evidence": str(output), "disabled": [p["id"] for p in research], "mode": evidence["mode"]}, indent=2))
