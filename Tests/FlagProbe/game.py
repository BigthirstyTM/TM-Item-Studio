"""Small recorded client for the installed localhost Trackmania test bridge.

Pass a tool name and pipe its JSON input on stdin. All responses are retained.
Uses the installed bridge's own transport implementation.
"""
import datetime
import importlib.util
import json
from pathlib import Path
import sys

helper = Path.home() / "OpenplanetPlugins/tm-control-mcp/tools/call.py"
spec = importlib.util.spec_from_file_location("tm_bridge", helper)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)

destination = Path(__file__).resolve().parents[2] / "artifacts/flag-research-20261021"


def call(tool, payload=None):
    request = {"route": "call", "tool": tool, "input": payload or {}}
    record = {"at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "request": request}
    try:
        response = bridge.send_request("127.0.0.1", 30006, 45, request)
        record["response"] = response
    except Exception as error:
        record["error"] = repr(error)
        raise
    finally:
        destination.mkdir(parents=True, exist_ok=True)
        with (destination / "game-calls.jsonl").open("a", encoding="utf-8") as output:
            output.write(json.dumps(record) + "\n")
    result = (response.get("data") or {}).get("result", {})
    output = result.get("output", {})
    if not response.get("ok") or not result.get("success") or output.get("ok") is False:
        raise RuntimeError(json.dumps(response))
    return output


if __name__ == "__main__":
    print(json.dumps(call(sys.argv[1], json.loads(sys.stdin.read() or "{}"))))
