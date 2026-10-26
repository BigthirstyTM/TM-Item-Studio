"""Offline package/hash and no-overwrite verification. No game/staging access."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
output = Path(sys.argv[1]).resolve()
assert output.is_relative_to(root / "artifacts")
report = output / "offline-verification.json"
assert not report.exists(), "Preserve previous verification evidence"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


manifest = json.loads((output / "manifest.json").read_text())
map_path = Path(manifest["outputMap"])
sidecar = json.loads(Path(str(map_path) + ".manifest.json").read_text())
assert sha(map_path) == manifest["outputSha256"] == sidecar["outputSha256"]
assert sidecar["manifestVersion"] == 1 and sidecar["anchoredObjectCount"] == 2
assert sidecar["nativePrimerPresent"] and sidecar["nativePrimerIndex"] == 0
assert not sidecar["nativeFlagEmbedded"]
assert len(sidecar["embeddedFiles"]) == 4
assert len(sidecar["expectedEmbeddedItemIds"]) == 1
for entry in sidecar["embeddedFiles"]:
    path = output / "files" / Path(entry["zipPath"]).name
    assert path.stat().st_size == entry["bytes"] and sha(path) == entry["sha256"]
assert sha(Path(manifest["sourceItem"])) == manifest["sourceItemSha256"]
assert sha(Path(manifest["sourceMap"])) == manifest["sourceMapSha256"]
for name, expected in manifest["dependencyHashes"].items():
    assert sha(Path(manifest["dependencySource"]) / name) == expected
before = {str(p.relative_to(output)): sha(p) for p in output.rglob("*") if p.is_file()}
result = subprocess.run(["dotnet", "run", "--project", str(root / "Tests/FlagBakedRotation"), "--no-build", "--", "--generate", str(output)], cwd=root, text=True, capture_output=True)
assert result.returncode != 0 and "choose an unused output directory" in result.stderr
after = {str(p.relative_to(output)): sha(p) for p in output.rglob("*") if p.is_file()}
assert before == after, "No-overwrite rejection must leave every prior output unchanged"
sources = [root / "Tests/FlagBakedRotation" / name for name in ["FlagBakedRotation.csproj", "Program.cs", "Fixture.cs", "README.md", "verify_fixture.py"]]
payload = {
    "scope": "Offline package and preservation verification only; no runtime result",
    "checks": {"mapAndSidecarHashExact": True, "fourPayloadHashesExact": True,
               "pluginFreeSidecarSchemaPresent": True, "sourceHashesUnchanged": True,
               "overwriteRefused": True, "allPriorOutputBytesUnchangedAfterRefusal": True},
    "priorOutputFiles": before,
    "generatorSources": [{"path": str(p.relative_to(root)), "sha256": sha(p)} for p in sources],
    "overwriteAttemptExitCode": result.returncode,
}
report.write_text(json.dumps(payload, indent=2) + "\n")
print(json.dumps({"report": str(report), "allOfflineChecksPassed": True, "runtimeTested": False}, indent=2))
