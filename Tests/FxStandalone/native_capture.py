"""Capture native viewport evidence, rejecting a reused stale screenshot name."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import time

parser = argparse.ArgumentParser()
parser.add_argument("request", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
helper_path = Path(r"C:\Users\PC\OpenplanetPlugins\tm-control-mcp\tools\call.py")
spec = importlib.util.spec_from_file_location("tm_bridge", helper_path)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)
started = time.time()
reply = bridge.send_request("127.0.0.1", 30006, 30,
    {"route": "call", "tool": "TakeScreenshot", "input": json.loads(args.request.read_text(encoding="utf-8-sig"))})
result = reply["data"]["result"]
if not result.get("success"):
    raise RuntimeError(reply)
source = Path(result["output"]["fullName"])
deadline = time.monotonic() + 12
while time.monotonic() < deadline:
    try:
        stat = source.stat()
        if stat.st_mtime >= started - .1 and stat.st_size > 0:
            time.sleep(.15)
            settled = source.stat()
            if stat.st_size == settled.st_size and stat.st_mtime == settled.st_mtime:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, args.output)
                evidence = {"source": str(source), "output": str(args.output.resolve()),
                    "requestedAtUnix": started, "writtenAtUnix": settled.st_mtime,
                    "bytes": settled.st_size, "freshCaptureVerified": True, "bridge": reply}
                args.output.with_suffix(".capture.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
                print(json.dumps(evidence, indent=2))
                break
    except (FileNotFoundError, PermissionError):
        pass
    time.sleep(.1)
else:
    raise RuntimeError("No fresh native screenshot appeared; existing files are not evidence: " + str(source))
