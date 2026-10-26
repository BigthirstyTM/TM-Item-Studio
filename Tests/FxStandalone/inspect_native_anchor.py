"""Read one actual editor anchor with the existing one-shot DEV probe.

Never preloads an asset or places an item. Retains fresh lifecycle evidence
beside the report, and rejects stale results or mismatched staged probe bytes.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import time
import uuid


def inspect(index: int | None, output: Path, path: str | None = None) -> dict:
    repo = Path(__file__).resolve().parents[2]
    home = Path.home()
    canonical = repo / "Tests/Openplanet/TMISFxCustomPlacement/Main.as"
    staged = home / "OpenplanetNext/Plugins/TMISFxCustomPlacement/Main.as"
    if canonical.read_bytes() != staged.read_bytes():
        raise ValueError("Canonical and staged native probe differ")
    if index is not None and index < 0:
        raise ValueError("Nonnegative anchor index required")
    if (index is None) == (path is None):
        raise ValueError("Specify exactly one anchor index or staged test-item path")
    if path is not None and (not path.startswith("Items/TMIS_FX_") or ".." in path):
        raise ValueError("Only a staged native FX test-item path is accepted")
    probe = home / "Documents/Trackmania/TMItemStudioPlacementProbe"
    request = probe / "fx-request.json"
    if request.exists():
        raise ValueError("A native probe request is already armed")
    run_id = "anchor-inspection-" + uuid.uuid4().hex
    log = home / "OpenplanetNext/Openplanet.log"
    boundary = log.stat().st_size
    parameters = {
        "armed": True, "runId": run_id, "inspectOnly": True,
        "inspectBitmap": True, "inspectRenderInfo": True,
    }
    parameters.update({"inspectAnchoredIndex": index} if index is not None else {"path": path})
    request.write_text(json.dumps(parameters), encoding="utf-8")
    helper = home / "OpenplanetPlugins/tm-control-mcp/tools/call.py"
    spec = importlib.util.spec_from_file_location("tm_bridge", helper)
    bridge = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(bridge)
    response = bridge.send_request("127.0.0.1", 30006, 20, {
        "route": "call", "tool": "ControlPlugin",
        "input": {"action": "load", "id": "TMISFxCustomPlacement"},
    })
    deadline = time.monotonic() + 12
    result_path = probe / "fx-result.json"
    while time.monotonic() < deadline:
        if result_path.exists():
            try:
                result = json.loads(result_path.read_text(encoding="utf-8-sig"))
                if result.get("runId") == run_id:
                    break
            except (OSError, ValueError):
                pass
        time.sleep(.15)
    else:
        raise TimeoutError("No fresh native anchor inspection result")
    fresh = log.read_bytes()[boundary:].decode("utf-8", errors="replace")
    evidence = {
        "sourceSha256": hashlib.sha256(canonical.read_bytes()).hexdigest().upper(),
        "logOffset": boundary, "freshLog": fresh, "bridge": response,
        "nativeItemMutation": False,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    output.with_suffix(".lifecycle.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    if "Loaded plugin 'TMISFxCustomPlacement'" not in fresh:
        raise RuntimeError("Fresh plugin load evidence missing")
    if "Script compilation failed" in fresh or ":  ERR :" in fresh:
        raise RuntimeError("Fresh game compilation diagnostics require review")
    if result.get("error"):
        raise RuntimeError(result["error"])
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--index", type=int)
    target.add_argument("--path")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = inspect(args.index, args.output, args.path)
    print(json.dumps(result, indent=2))
