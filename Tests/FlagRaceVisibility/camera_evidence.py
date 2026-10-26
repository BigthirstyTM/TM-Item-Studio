"""Existing receipt provenance and offline placement planning; no runtime/staging."""
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
out = Path(sys.argv[1]).resolve()
assert out.is_relative_to(root / "artifacts")
report = out / "offline-camera-and-preservation-check.json"
plot = out / "offline-camera-placement-plan.png"
assert not report.exists() and not plot.exists(), "Preserve previous evidence"
map_path = out / "TMIS_Gravity_Three_Shapes_Start_Visibility_NeedsNativeSave.Map.Gbx"
manifest = json.loads(Path(str(map_path) + ".manifest.json").read_text())
receipts = root / "artifacts/flag-selfcontained-20261022/native-validation-run/game-calls.jsonl"
records = [json.loads(line) for line in receipts.read_text().splitlines() if line.strip()]
vehicle = next((i+1,r) for i,r in enumerate(records) if r["request"]["tool"] == "GetVehicleState" and r["response"]["data"]["result"]["output"].get("state", {}).get("available"))
camera = next((i+1,r) for i,r in enumerate(records) if r["request"]["tool"] == "ControlPlayCamera" and "before" in r["response"]["data"]["result"]["output"])
state = vehicle[1]["response"]["data"]["result"]["output"]["state"]
before = camera[1]["response"]["data"]["result"]["output"]["before"]
assert state["position"] == [784,10.014,400]
assert before["render"]["position"] == [784,13.314,395] and before["camType"]["name"] == "cam1"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def infer(p):
    # Exact generator rigid mapping, applied to an older flat-map receipt only as a hypothesis.
    x,y,z = p[0]-768,p[1]-8,p[2]-384
    a = math.pi/12
    return [768+x,64+y*math.cos(a)-z*math.sin(a),384+y*math.sin(a)+z*math.cos(a)]


car = infer(state["position"])
cam = infer(before["render"]["position"])
assert sha(map_path) == manifest["outputSha256"]
assert sha(Path(manifest["sourceMap"])) == manifest["sourceSha256"]
snapshot = {str(p.relative_to(out)):sha(p) for p in out.rglob("*") if p.is_file()}
attempt = subprocess.run(["dotnet","run","--project",str(root/"Tests/FlagRaceVisibility"),"--no-build","--","--generate",str(out)],cwd=root,text=True,capture_output=True)
assert attempt.returncode != 0 and "choose an unused output directory" in attempt.stderr
assert snapshot == {str(p.relative_to(out)):sha(p) for p in out.rglob("*") if p.is_file()}

im=Image.new("RGB",(1500,800),(248,250,252)); d=ImageDraw.Draw(im)
font=ImageFont.truetype("C:/Windows/Fonts/arial.ttf",18)
small=ImageFont.truetype("C:/Windows/Fonts/arial.ttf",15)
title=ImageFont.truetype("C:/Windows/Fonts/arial.ttf",23)
d.text((28,20),"Offline placement plan - not game footage or a visibility verdict",fill=(15,23,42),font=title)
colours=[(37,99,235),(192,38,211),(5,150,105)]; short=["Swallowtail","Long250","Round"]
def top(x,z): return (70+(x-748)*9.2,650-(z-384)*7)
def side(z,y): return (800+(z-384)*8,650-(y-32)*13)
d.rectangle((45,85,710,680),outline=(148,163,184),width=2)
d.rectangle((760,85,1450,680),outline=(148,163,184),width=2)
d.text((60,100),"Top view: world X / Z (m)",fill=(15,23,42),font=font)
d.text((780,100),"Side view: world Z / Y (m)",fill=(15,23,42),font=font)
d.rectangle((top(768,458)[0],top(768,458)[1],top(800,384)[0],top(800,384)[1]),fill=(226,232,240))
d.text((top(768,458)[0]+8,top(768,458)[1]+8),"road X768..800",fill=(71,85,105),font=small)
for x in [758,768,784,800,802]:
    q=top(x,384);d.text((q[0]-12,654),str(x),fill=(100,116,139),font=small)
for z in [400,430,446]:
    q=top(748,z);d.text((48,q[1]-6),str(z),fill=(100,116,139),font=small)
a=side(384,64);b=side(458,64-(458-384)*math.tan(math.pi/12))
d.line((a,b),fill=(100,116,139),width=5)
for p,c,label in [([cam[0],cam[2]],(17,24,39),"camera"),([car[0],car[2]],(71,85,105),"car")]:
    q=top(*p);d.ellipse((q[0]-6,q[1]-6,q[0]+6,q[1]+6),fill=c);d.text((q[0]+10,q[1]-8),label,fill=c,font=small)
for p,c,label in [([cam[2],cam[1]],(17,24,39),"camera"),([car[2],car[1]],(71,85,105),"car")]:
    q=side(*p);d.ellipse((q[0]-6,q[1]-6,q[0]+6,q[1]+6),fill=c);d.text((q[0]+10,q[1]-8),label,fill=c,font=small)
points=[]
for i,(row,color,name) in enumerate(zip(manifest["placement"],colours,short)):
    p=row["outputPosition"];q=top(p["X"],p["Z"]);cp=top(cam[0],cam[2])
    d.line((cp,q),fill=color,width=1);d.ellipse((q[0]-6,q[1]-6,q[0]+6,q[1]+6),fill=color)
    d.text((q[0]-95 if p["X"]<784 else q[0]+8,q[1]-22),name,fill=color,font=small)
    plane=64-(p["Z"]-384)*math.tan(math.pi/12)
    d.line((side(p["Z"],p["Y"]),side(p["Z"],p["Y"]+13)),fill=color,width=2)
    # Right/left same-Z cloths are overlaid in side view; draw a small labelled offset.
    offset=8 if i==2 else 0
    lo=side(p["Z"],plane+.7);hi=side(p["Z"],plane+5.3)
    d.line(((lo[0]+offset,lo[1]),(hi[0]+offset,hi[1])),fill=color,width=7)
    d.text((hi[0]+12,hi[1]-15+22*i),name,fill=color,font=small)
    points.append({"shape":name,"polePosition":[p["X"],p["Y"],p["Z"]],"approximateAttachmentHorizontalAngleDegrees":math.degrees(math.atan2(p["X"]-cam[0],p["Z"]-cam[2])),"inferenceOnly":True})
d.text((60,706),"Thick coloured segments: estimated cloth vertical range. Thin lines: fixed poles. Grey: sloped road model-origin plane.",fill=(71,85,105),font=small)
d.text((60,738),"Camera/car points use earlier flat native receipts; actual gravity camera, FOV, road surface and visibility remain unmeasured.",fill=(71,85,105),font=small)
im.save(plot)
source_files=[root/"Tests/FlagRaceVisibility"/n for n in ("FlagRaceVisibility.csproj","Program.cs","README.md","camera_evidence.py")]+[root/"Tests/FlagMapRepair/MapArchive.cs"]
payload={
    "scope":"Offline planning, source provenance and preservation guard only; no current game/camera/visibility result",
    "outputMap":str(map_path),"outputSha256":sha(map_path),"overwriteRefusedWithoutChanges":True,
    "existingReceiptSource":{"path":str(receipts.relative_to(root)),"sha256":sha(receipts),"vehicleLine":vehicle[0],"vehicleAt":vehicle[1]["at"],"vehiclePosition":state["position"],"cameraLine":camera[0],"cameraAt":camera[1]["at"],"cameraBeforePosition":before["render"]["position"],"cameraBeforeType":before["camType"]["name"],"cameraBeforeFovValue":before["render"]["fov"],"fovInterpretationNotEstablished":True},
    "inference":{"formula":"P_gravity=(768,64,384)+Rx(15deg)*(P_flat-(768,8,384))","carCentre":car,"cameraPosition":cam,"actualGravityCameraNotMeasured":True,"points":points},
    "cameraFootageEvidence":{"path":"artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/analysis/repeat-run-3s-contact.jpg","sha256":sha(root/"artifacts/flag-selfcontained-20261022/gravity-shadowbaked-localrace-cold-no-openplanet/analysis/repeat-run-3s-contact.jpg"),"supports":"Ordinary car/chase-camera view and low old-flag visibility limitation; does not depict this new candidate"},
    "generatorSources":[{"path":str(p.relative_to(root)),"sha256":sha(p)} for p in source_files],
    "planningPlot":{"path":plot.name,"sha256":sha(plot),"isGameFootage":False},
    "outputFilesBeforeVerification":snapshot,
}
report.write_text(json.dumps(payload,indent=2)+"\n")
print(json.dumps({"report":str(report),"plot":str(plot),"sourceHashesChecked":True,"overwriteGuardPassed":True,"runtimeTested":False},indent=2))
