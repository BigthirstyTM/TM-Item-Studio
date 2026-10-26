"""Offline packaging of actual Desktop Duplication frames; never synthesizes motion."""
from __future__ import annotations

import hashlib
import json
from fractions import Fraction
from pathlib import Path
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / "artifacts/flag-selfcontained-20261022/embedded-v6-primer-no-openplanet"
OUT = BASE / "final"
FINAL = OUT / "embedded-custom-flags-no-openplanet-1080p.mp4"
NAMES = ["overview", "swallowtail", "long", "round"]
TITLES = ["ALL THREE SHAPES | Round / long / swallowtail",
          "SWALLOWTAIL | Real cloth deformation", "LONG CLOTH | Real cloth deformation",
          "ROUND CLOTH | Real cloth deformation"]
TIME_BASE = Fraction(1, 15360)


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def probe(path):
    return json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-count_frames", "-select_streams", "v:0",
        "-show_entries", "stream=codec_name,width,height,r_frame_rate,avg_frame_rate,nb_frames,nb_read_frames,time_base,start_pts,duration_ts,duration",
        "-show_entries", "format=duration,size", "-of", "json", str(path)]))


def frames(path):
    return json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-select_streams", "v:0", "-show_frames",
        "-show_entries", "frame=pts,best_effort_timestamp,duration", "-of", "json", str(path)]))["frames"]


def check_evidence():
    for name in ["before", "after", "final"]:
        modules = load(BASE / f"modules-{name}.json")
        assert not any("openplanet" in m["FileName"].lower() for m in modules)
        proxy = [m for m in modules if m["ModuleName"].lower() == "dinput8.dll"]
        assert len(proxy) == 1 and proxy[0]["FileName"].lower() == r"c:\windows\system32\dinput8.dll"
        absence = load(BASE / f"absence-{name}.json")
        assert absence["openplanetAbsent"] and absence["systemDinput8"] and absence["logUnchanged"]
    before, after = load(BASE / "process-before.json"), load(BASE / "process-after.json")
    assert before["Id"] == after["Id"] and before["StartTime"] == after["StartTime"]
    boundary = load(BASE / "boundary.json")
    assert digest(Path(boundary["held"])) == boundary["proxySha256"].lower()
    log = Path.home() / "OpenplanetNext/Openplanet.log"
    assert log.stat().st_size == boundary["logSize"]
    assert log.stat().st_mtime_ns == boundary["logMtimeNs"]
    isolated = load(BASE / "loose-isolation.json")
    assert not isolated["matchingLooseFiles"]
    evidence_paths = ["modules-before.json", "modules-after.json", "absence-before.json", "absence-after.json",
                      "process-before.json", "process-after.json", "boundary.json", "loose-isolation.json",
                      "map.json", "framing-stage.json", "modules-final.json", "absence-final.json"]
    return {"openplanetModuleAbsentBeforeAndAfter": True,
            "systemDinput8LoadedBeforeAndAfter": True, "sameGameProcessBeforeAndAfter": True,
            "openplanetAbsentInFinalModuleCheck": True,
            "openplanetLogUnchangedAtFinalization": True,
            "looseFixtureFilesAbsentInRecordedIsolationCheck": True,
            "gameProcessId": before["Id"],
            "evidenceFiles": [{"name": p, "sha256": digest(BASE / p)} for p in evidence_paths]}


def read_gray_crop(path, crop):
    x, y, w, h = crop
    data = subprocess.check_output([
        "ffmpeg", "-v", "error", "-i", str(path), "-an", "-vf", f"format=gray,crop={w}:{h}:{x}:{y}:exact=1",
        "-fps_mode", "passthrough", "-enc_time_base", "1:15360", "-f", "rawvideo", "-"], stderr=subprocess.PIPE)
    return np.frombuffer(data, dtype=np.uint8).reshape((-1, h, w))


def metrics(path, crop, expected_frames):
    values = read_gray_crop(path, crop)
    assert len(values) == expected_frames
    diffs = np.abs(np.diff(values.astype(np.float32), axis=0)).mean(axis=(1, 2))
    return {"cropXYWH": crop, "decodedFrames": len(values),
            "distinctCropFrames": len({hashlib.sha256(f.tobytes()).digest() for f in values}),
            "medianAdjacentMeanAbsoluteDifference": float(np.median(diffs)),
            "meanAdjacentMeanAbsoluteDifference": float(diffs.mean()),
            "p95AdjacentMeanAbsoluteDifference": float(np.percentile(diffs, 95)),
            "meanTemporalPixelStandardDeviation": float(values.astype(np.float32).std(axis=0).mean())}


def make_contact_sheet(clips, crops):
    font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 25)
    small = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 20)
    sheet = Image.new("RGB", (1920, 1480), "#111923")
    draw = ImageDraw.Draw(sheet)
    draw.text((22, 16), "REAL GAME FRAMES | NO OPENPLANET | EMBEDDED CUSTOM FLAGS | NATIVE PRIMER PRESENT", font=font, fill="white")
    draw.text((22, 52), "Each row uses a fixed camera and crop at three original capture times. No motion was generated.", font=small, fill="#d0d7df")
    for row, clip in enumerate(clips):
        y = 92 + row * 340
        draw.text((22, y), TITLES[row], font=font, fill="white")
        for col, sec in enumerate([1.0, 3.0, 5.5]):
            path = OUT / f"{clip['name']}-t{sec:g}.png"
            if not path.exists():
                subprocess.run(["ffmpeg", "-v", "error", "-ss", str(sec), "-i", str(clip["path"]), "-frames:v", "1", str(path)], check=True)
            picture = Image.open(path).convert("RGB")
            if clip["name"] == "overview":
                # All three cloths and their poles; the full final video preserves the whole viewport.
                region = picture.crop((630, 615, 1780, 1000))
            else:
                x, cy, w, h = crops[clip["name"]]["contact"]
                region = picture.crop((x, cy, x + w, cy + h))
            region.thumbnail((620, 270), Image.Resampling.LANCZOS)
            x = col * 640 + (640 - region.width) // 2
            sheet.paste(region, (x, y + 40))
            draw.text((col * 640 + 22, y + 311), f"Original capture t={sec:g}s", font=small, fill="#d0d7df")
    sheet.save(OUT / "motion-contact-sheet.jpg", quality=94)


def main():
    resume = sys.argv[1:] == ["--resume"]
    assert not sys.argv[1:] or resume, "Only --resume is supported."
    assert not OUT.exists() or resume, "Preserve existing evidence; use --resume only to finish incomplete own outputs."
    assert not (OUT / "validation.json").exists(), "A validated result already exists; preserve it."
    evidence = check_evidence()
    OUT.mkdir(exist_ok=resume)
    clips = []
    cursor = 0
    for name in NAMES:
        path = BASE / f"{name}-60hz.mp4"
        metadata = probe(path)
        stream = metadata["streams"][0]
        fs = frames(path)
        assert stream["width"] == 2560 and stream["height"] == 1440
        assert Fraction(stream["time_base"]) == TIME_BASE and stream["start_pts"] == 0
        assert len(fs) == int(stream["nb_read_frames"])
        # Source capture has repeated and locally irregular PTS. Retain all actual frames.
        # Segment boundaries cover the last source frame even when MP4 duration understates it.
        end_tick = max(int(f["pts"]) + int(f.get("duration", 256)) for f in fs)
        duration_ticks = max(int(stream["duration_ts"]), end_tick)
        pts = [int(f["pts"]) for f in fs]
        be_pts = [int(f["best_effort_timestamp"]) for f in fs]
        clips.append({"name": name, "path": path, "sha256": digest(path), "metadata": metadata,
                      "frames": len(fs), "durationTicks": duration_ticks, "startTicks": cursor,
                      "captureFramesPerSpanSecond": len(fs) / float(duration_ticks * TIME_BASE),
                      "sourceDuplicatePts": len(pts) - len(set(pts)),
                      "sourceNonIncreasingDecodedPts": sum(b <= a for a, b in zip(pts, pts[1:])),
                      "sourceBestEffortPts": be_pts})
        cursor += duration_ticks
    concat = OUT / "sources.ffconcat"
    concat.write_text("ffconcat version 1.0\n" + "".join(
        f"file '{c['path'].as_posix()}'\nduration {float(c['durationTicks'] * TIME_BASE):.12f}\n" for c in clips), encoding="utf-8")
    (OUT / "title.txt").write_text("NO OPENPLANET | EMBEDDED CUSTOM FLAGS | NATIVE FLAG PRIMER PRESENT", encoding="utf-8")
    (OUT / "note.txt").write_text("Actual game capture | 54-57 captured fps | No interpolation or speed change | One client, editor test", encoding="utf-8")
    filters = ["scale=1920:1080:flags=lanczos", "setsar=1",
               "drawbox=x=0:y=0:w=iw:h=63:color=black@0.82:t=fill",
               "drawbox=x=0:y=982:w=iw:h=98:color=black@0.82:t=fill",
               r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='title.txt':x=24:y=18:fontsize=27:fontcolor=white",
               r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='note.txt':x=24:y=1046:fontsize=21:fontcolor=white"]
    for i, clip in enumerate(clips):
        (OUT / f"chapter-{i}.txt").write_text(TITLES[i], encoding="utf-8")
        start = float(clip["startTicks"] * TIME_BASE)
        end = float((clip["startTicks"] + clip["durationTicks"]) * TIME_BASE)
        filters.append(r"drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':textfile='chapter-" + str(i) +
                       f".txt':x=24:y=1000:fontsize=28:fontcolor=white:enable='gte(t,{start:.9f})*lt(t,{end:.9f})'")
    if not FINAL.exists():
        with (OUT / "encode.log").open("w", encoding="utf-8") as log:
            subprocess.run(["ffmpeg", "-hide_banner", "-f", "concat", "-safe", "0", "-i", concat.name,
                            "-an", "-vf", ",".join(filters), "-c:v", "libx264", "-preset", "fast", "-crf", "18",
                            "-bf", "0", "-pix_fmt", "yuv420p", "-fps_mode", "passthrough",
                            "-enc_time_base", "1:15360", "-video_track_timescale", "15360",
                            "-movflags", "+faststart", FINAL.name], cwd=OUT, stdout=log, stderr=log, check=True)
    final_metadata = probe(FINAL)
    final_frames = frames(FINAL)
    assert len(final_frames) == sum(c["frames"] for c in clips) == 1600
    assert final_metadata["streams"][0]["width"] == 1920 and final_metadata["streams"][0]["height"] == 1080
    final_pts = [int(f["pts"]) for f in final_frames]
    assert all(b > a for a, b in zip(final_pts, final_pts[1:]))
    assert len(set(b - a for a, b in zip(final_pts, final_pts[1:]))) > 1, "Final output must remain variable frame rate."
    # Crop configuration is independently inspected against original game frames.
    crop_path = BASE.parent / "video-crop-audit/crops.json"
    crops = load(crop_path)
    motion = []
    for clip in clips:
        configuration = crops[clip["name"]]
        background = metrics(clip["path"], configuration["background"], clip["frames"])
        for shape, crop in configuration["cloth"].items():
            cloth = metrics(clip["path"], crop, clip["frames"])
            ratio = cloth["meanAdjacentMeanAbsoluteDifference"] / max(background["meanAdjacentMeanAbsoluteDifference"], 1e-8)
            assert cloth["distinctCropFrames"] > 100 and ratio > 5, "Cloth motion must exceed fixed background variation."
            motion.append({"clip": clip["name"], "shape": shape, "cloth": cloth, "background": background,
                           "clothToBackgroundMeanDifferenceRatio": ratio})
    make_contact_sheet(clips, crops)
    for clip in clips:
        assert digest(clip["path"]) == clip["sha256"], "Raw video changed."
    evidence_end = check_evidence()
    assert evidence == evidence_end
    timeline_changes = []
    index = 0
    for clip in clips:
        actual = final_pts[index:index + clip["frames"]]
        expected = [p + clip["startTicks"] for p in clip["sourceBestEffortPts"]]
        deltas = [a - e for a, e in zip(actual, expected)]
        timeline_changes.append({"clip": clip["name"], "startSeconds": float(clip["startTicks"] * TIME_BASE),
                                "frames": clip["frames"], "changedPtsCountVersusSourceBestEffort": sum(d != 0 for d in deltas),
                                "minPtsDeltaTicks": min(deltas), "maxPtsDeltaTicks": max(deltas),
                                "maxAbsolutePtsDeltaSeconds": float(max(map(abs, deltas)) * TIME_BASE)})
        index += clip["frames"]
    serializable_clips = [{k: str(v) if isinstance(v, Path) else v for k, v in c.items()
                           if k != "sourceBestEffortPts"} for c in clips]
    validation = {
        "operation": "offline-finalize-embedded-plugin-free-capture", "sources": serializable_clips,
        "final": {"path": str(FINAL), "sha256": digest(FINAL), "metadata": final_metadata},
        "finalFrameCount": len(final_frames), "sumSourceFrameCounts": sum(c["frames"] for c in clips),
        "allActualSourceFramesRetained": True, "rawFilesUnchanged": True,
        "variableFrameRateRetained": True, "synthesizedFrames": 0, "interpolationPerformed": False,
        "speedChangePerformed": False, "finalTimeBase": "1/15360", "timelineComparison": timeline_changes,
        "timestampNote": "NVENC source PTS include repeats and local irregularities. Passthrough encoding retains every real source frame; the MP4 muxer minimally makes timestamps monotonic. Segment duration includes the last source frame, even where source container duration is shorter. This is timestamp repair, not generated motion or a fixed-rate conversion.",
        "captureMethod": "Original 2560x1440 Desktop Duplication/NVENC captures; requested 60 Hz, measured actual frame rate reported per clip.",
        "editing": "Lanczos reduction to 1920x1080, English text bands and direct cuts. No crossfades, motion generation or frame-rate conversion.",
        "chapterOrder": NAMES, "clothMotion": motion, "absenceEvidence": evidence,
        "cropAudit": {"path": str(crop_path), "sha256": digest(crop_path)},
        "mapContext": {"overviewAndLong": load(BASE / "map.json"), "framingClones": load(BASE / "framing-stage.json"),
                       "framingNote": "Swallowtail and round use map clones translating all three custom anchors by +20X and -20X respectively. The native primer and the complete six-payload embedded ZIP remain unchanged. Long uses the original near-camera map."},
        "scope": "Observed rendering and cloth deformation of three custom shapes embedded with their dependencies, with a native flag primer present, on one local client in the editor with no Openplanet module. This does not prove the primer is required, an independent-client result, a full playable map, native save persistence or kinematic behavior.",
        "rejectedInputs": ["01-overview-raw.mp4", "02-long-raw.mp4"],
        "contactSheet": str(OUT / "motion-contact-sheet.jpg")}
    (OUT / "validation.json").write_text(json.dumps(validation, indent=2), encoding="utf-8")
    print(json.dumps(validation, indent=2))


if __name__ == "__main__":
    main()
