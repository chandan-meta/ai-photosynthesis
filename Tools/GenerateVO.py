"""Dr. Curio voice-over: neural child voice (Microsoft Edge TTS, en-US-AnaNeural) -> Resources/VO/<id>.wav (22050 Hz mono 16-bit).
    python Tools/GenerateVO.py            # lines without a clip
    python Tools/GenerateVO.py --force    # every line
    python Tools/GenerateVO.py --only s3_good,s3_intro
Input: Assets/Photosynthesis/Resources/curio_lines.txt (id|text). Needs: pip install edge-tts, ffmpeg on PATH, internet.
Delivery: cute, excited, curious young guide. Rate +10 %, trailing silence trimmed (Dialogue waits for the clip length).
"""
import argparse, asyncio, os, re, subprocess, sys, tempfile
import edge_tts

VOICE, RATE, PITCH = "en-US-AnaNeural", "+10%", "+0Hz"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LINES = os.path.join(ROOT, "Assets/Photosynthesis/Resources/curio_lines.txt")
OUT = os.path.join(ROOT, "Assets/Photosynthesis/Resources/VO")
SUB = {"₀":"0","₁":"1","₂":"2","₃":"3","₄":"4","₅":"5","₆":"6","₇":"7","₈":"8","₉":"9"}

def spoken(text):
    t = text.strip().lstrip("…. ")                     # a leading ellipsis continues the previous line
    t = "".join(SUB.get(c, c) for c in t)
    t = t.replace("CO2", "C O 2").replace("H2O", "H 2 O").replace("O2", "O 2").replace("C6H12O6", "C 6, H 12, O 6")
    t = t.replace("→", " to ").replace("×", " times ").replace("—", ", ").replace("–", ", ")
    t = re.sub(r"[✓✕•]", " ", t)
    return re.sub(r"\s+", " ", t).strip()

async def one(sem, id_, text, mp3):
    async with sem:
        for attempt in range(3):
            try:
                await edge_tts.Communicate(spoken(text), VOICE, rate=RATE, pitch=PITCH).save(mp3); return True
            except Exception as e:
                err = e; await asyncio.sleep(1.5)
        print(f"  FAILED {id_}: {err}"); return False

def to_wav(mp3, wav):
    # mp3 -> 22050 Hz mono 16-bit; trailing silence trimmed to ~0.18 s (reverse / silenceremove / reverse / apad)
    af = "areverse,silenceremove=start_periods=1:start_threshold=-42dB:start_silence=0.08,areverse,apad=pad_dur=0.18"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", mp3, "-af", af, "-ar", "22050", "-ac", "1", "-sample_fmt", "s16", wav], check=True)

async def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--force", action="store_true"); ap.add_argument("--only", default="")
    a = ap.parse_args(); only = {s.strip() for s in a.only.split(",") if s.strip()}
    os.makedirs(OUT, exist_ok=True)
    todo = []
    for raw in open(LINES, encoding="utf-8"):
        l = raw.strip()
        if not l or l.startswith("#") or "|" not in l: continue
        id_, text = l.split("|", 1); id_ = id_.strip()
        if only and id_ not in only: continue
        wav = os.path.join(OUT, id_ + ".wav")
        if os.path.exists(wav) and not a.force: continue
        todo.append((id_, text.strip(), wav))
    print(f"{len(todo)} lines to synthesise with {VOICE}")
    sem = asyncio.Semaphore(4); tmp = tempfile.mkdtemp()
    jobs = [(i, t, w, os.path.join(tmp, i + ".mp3")) for i, t, w in todo]
    ok = await asyncio.gather(*[one(sem, i, t, m) for i, t, w, m in jobs])
    made = 0; secs = 0.0
    for (i, t, w, m), good in zip(jobs, ok):
        if not good: continue
        to_wav(m, w); os.remove(m); made += 1
        secs += (os.path.getsize(w) - 44) / (22050 * 2)
        print(f"  {i:<22} {(os.path.getsize(w) - 44) / (22050 * 2):5.1f}s  {t}")
    print(f"VO summary: {made} generated ({secs:.0f} s of audio), {len(todo) - made} failed. Output: {OUT}")
    sys.exit(0 if made == len(todo) else 1)

asyncio.run(main())
