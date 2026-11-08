# inbox の Tripo の zip を1つずつ解凍し、process_tiers.py で中・高・最高を作る（1 つずつ別の Blender、低い優先度）。
# 使い方: python Tools/blender/process_tiers_all.py [名前 ...]   （名前を付けるとそれだけ。試しに 1 つ作るとき）
import os, sys, zipfile, shutil, subprocess

BLENDER = "D:/Program Files (x86)/Blender/blender.exe"
SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "process_tiers.py")
ROOT = "D:/rts-art/tripo"
TMP = ROOT + "/tmp_tiers"
JOBS = [(ROOT + "/inbox/buildings", ROOT + "/processed_tiers", "building"),
        (ROOT + "/inbox/resources", ROOT + "/processed_tiers_res", "resource")]
only = set(sys.argv[1:])
os.makedirs(JOBS[0][1], exist_ok=True)
log = open(os.path.join(JOBS[0][1], "process.log"), "a", encoding="utf-8")
for inbox, out, kind in JOBS:
    os.makedirs(out, exist_ok=True)
    for z in sorted(os.listdir(inbox)):
        if not z.endswith(".zip"):
            continue
        name = z[:-4]
        if only and name not in only:
            continue
        dst = os.path.join(out, name)
        if os.path.exists(os.path.join(dst, name + "_M_LOD1.fbx")):
            continue
        shutil.rmtree(TMP, ignore_errors=True)
        with zipfile.ZipFile(os.path.join(inbox, z)) as f:
            f.extractall(TMP)
        fbx = [x for x in os.listdir(TMP) if x.endswith(".fbx")][0]
        r = subprocess.run([BLENDER, "-b", "--factory-startup", "--python", SCRIPT, "--",
                            os.path.join(TMP, fbx), dst, name, kind], capture_output=True, text=True,
                           encoding="utf-8", errors="replace",
                           creationflags=0x00004000)  # BELOW_NORMAL_PRIORITY_CLASS
        lines = [l for l in r.stdout.splitlines() if l.startswith(("TRIS", "STEP", "TEX", "STUCK"))]
        log.write(name + " exit=" + str(r.returncode) + " " + " | ".join(lines) + "\n")
        if r.returncode != 0:
            log.write(r.stderr[-2000:] + "\n")
        log.flush()
shutil.rmtree(TMP, ignore_errors=True)
open(os.path.join(JOBS[0][1], "process.done"), "w").write("DONE")
