"""Dr. Curio voice-over: neural child voice (Microsoft Edge TTS, en-US-AnaNeural) -> Resources/VO (22050 Hz mono 16-bit WAV).
    python Tools/GenerateVO.py            # story lines without a clip  (Resources/curio_lines.txt -> VO/<id>.wav)
    python Tools/GenerateVO.py --force    # every story line
    python Tools/GenerateVO.py --only s3_good,s3_intro
    python Tools/GenerateVO.py --kb       # Ask-AI replies: every fixed sentence -> VO/kb/<key>.wav (Dialogue.KbClip joins them)
Needs: pip install edge-tts numpy, ffmpeg on PATH, internet.
Delivery: cute, excited, curious young guide. Rate +10 %. Pauses inside a clip are capped at 0.32 s, the head is trimmed to 0.04 s
and the tail to 0.18 s (Dialogue waits for the clip length, so dead air in a clip is dead air in the module).
--kb keys: FNV-1a over the sentence's lower-case ASCII letters and digits, the same as Dialogue.KbKey. Sentences are split on
(?<=[.!?…:])\s+ after the same text transform as CurioKnowledge.Spoken(). A manifest goes to Tools/kb_manifest.tsv.
"""
import argparse, asyncio, os, re, subprocess, sys, tempfile, wave
import numpy as np
import edge_tts

VOICE, RATE, PITCH, SR = "en-US-AnaNeural", "+10%", "+0Hz", 22050
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LINES = os.path.join(ROOT, "Assets/Photosynthesis/Resources/curio_lines.txt")
OUT = os.path.join(ROOT, "Assets/Photosynthesis/Resources/VO")
RUNTIME = os.path.join(ROOT, "Assets/Photosynthesis/Scripts/Runtime")
SUB = {"₀": "0", "₁": "1", "₂": "2", "₃": "3", "₄": "4", "₅": "5", "₆": "6", "₇": "7", "₈": "8", "₉": "9"}
SPLIT = re.compile(r"(?<=[.!?…:])\s+")


def spoken(text):
    """Text sent to the TTS engine (pronunciation only; subtitles keep the original)."""
    t = text.strip().lstrip("…. ")                     # a leading ellipsis continues the previous line
    t = "".join(SUB.get(c, c) for c in t)
    t = t.replace("CO2", "C O 2").replace("H2O", "H 2 O").replace("O2", "O 2").replace("C6H12O6", "C 6, H 12, O 6")
    t = t.replace("→", " to ").replace("×", " times ").replace("—", ", ").replace("–", ", ")
    t = re.sub(r"[✓✕•]", " ", t)
    return re.sub(r"\s+", " ", t).strip()


def curio_spoken(s):
    """Python twin of CurioKnowledge.Spoken(): drops (formulas), names the molecules."""
    s = re.sub(r"\s*\([^)]*[₀-₉→][^)]*\)", "", s)
    s = s.replace("C₆H₁₂O₆", "glucose").replace("CO₂", "carbon dioxide").replace("H₂O", "water").replace("O₂", "oxygen")
    return s.replace(" → ", " makes ").replace("→", " makes ").replace("  ", " ").strip()


def kb_key(sentence):
    """Python twin of Dialogue.KbKey."""
    h = 2166136261
    norm = re.sub(r"[^a-z0-9]", "", sentence.lower())
    for ch in norm:
        h ^= ord(ch); h = (h * 16777619) & 0xFFFFFFFF
    return f"{h:08x}" if norm else ""


def tighten(x, sr=SR, thr=260, cap=0.32, head=0.04, tail=0.18):
    """Trim the head / tail and cap every pause inside the clip (int16 mono)."""
    win = int(sr * 0.01); n = len(x) // win
    if n == 0: return x
    loud = np.abs(x[: n * win].reshape(n, win)).max(axis=1) >= thr
    idx = np.flatnonzero(loud)
    if idx.size == 0: return x[: int(sr * 0.2)]
    first, last = idx[0], idx[-1]
    keep = []                                               # (start, end) sample ranges to keep
    start = max(0, first * win - int(head * sr))
    i = first
    while i <= last:
        if loud[i]: i += 1; continue
        j = i
        while j <= last and not loud[j]: j += 1            # silent run [i, j) inside the speech
        if (j - i) * win > cap * sr:
            half = int(cap * sr / 2)
            keep.append((start, i * win + half)); start = j * win - half
        i = j
    keep.append((start, min(len(x), (last + 1) * win + int(tail * sr))))
    return np.concatenate([x[a:b] for a, b in keep])


def to_wav(mp3, wav):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", mp3, "-f", "s16le", "-ac", "1", "-ar", str(SR), "-"], check=True, capture_output=True).stdout
    y = tighten(np.frombuffer(raw, dtype=np.int16))
    with wave.open(wav, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(y.astype(np.int16).tobytes())
    return len(y) / SR


async def tts(sem, text, mp3):
    async with sem:
        err = None
        for _ in range(3):
            try:
                await edge_tts.Communicate(spoken(text), VOICE, rate=RATE, pitch=PITCH).save(mp3); return True
            except Exception as e:
                err = e; await asyncio.sleep(1.5)
        print(f"  FAILED {text[:50]!r}: {err}"); return False


def story_jobs(force, only):
    jobs = []
    for raw in open(LINES, encoding="utf-8"):
        l = raw.strip()
        if not l or l.startswith("#") or "|" not in l: continue
        id_, text = l.split("|", 1); id_ = id_.strip()
        if only and id_ not in only: continue
        wav = os.path.join(OUT, id_ + ".wav")
        if os.path.exists(wav) and not force: continue
        jobs.append((id_, text.strip(), wav))
    return jobs


def kb_sentences():
    """Every fixed sentence Dr. Curio can say as an Ask-AI reply (CurioKnowledge, AskAI, Story.WorldAction)."""
    ck = open(os.path.join(RUNTIME, "Learning/CurioKnowledge.cs"), encoding="utf-8").read()
    ask = open(os.path.join(RUNTIME, "Learning/AskAI.cs"), encoding="utf-8").read()
    story = open(os.path.join(RUNTIME, "Story/Story.cs"), encoding="utf-8").read()
    story = story[story.index("public string WorldAction("):]
    memories = [m.replace('\\"', '"') for m in re.findall(r'memory\s*=\s*"((?:[^"\\]|\\.)*)"', ck)]
    lit = re.compile(r'(\$?)@?"((?:[^"\\\n]|\\.)*)"')
    texts = []
    for src in (ck, ask, story):
        for interp, body in lit.findall(src):
            body = body.replace('\\"', '"').replace("\\n", " ")
            if interp and "{" in body:
                if all("memory" in ph for ph in re.findall(r"\{([^{}]*)\}", body)):
                    texts += [re.sub(r"\{[^{}]*\}", m, body) for m in memories]
                continue
            texts.append(body)
    out = {}
    for t in texts:
        for s in SPLIT.split(curio_spoken(t).strip()):
            s = s.strip()
            if len(re.sub(r"[^A-Za-z]", "", s)) < 2 or re.search(r"[\\\[\]{}|^$@#/<>=]", s): continue
            if not re.search(r"[.!?…:][\"”’')]*$", s): continue
            k = kb_key(s)
            if k: out.setdefault(k, s)
    return out


async def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--force", action="store_true"); ap.add_argument("--only", default=""); ap.add_argument("--kb", action="store_true")
    a = ap.parse_args(); only = {s.strip() for s in a.only.split(",") if s.strip()}
    if a.kb:
        kdir = os.path.join(OUT, "kb"); os.makedirs(kdir, exist_ok=True)
        sents = kb_sentences()
        with open(os.path.join(ROOT, "Tools/kb_manifest.tsv"), "w", encoding="utf-8") as f:
            for k, s in sorted(sents.items(), key=lambda kv: kv[1]): f.write(f"{k}\t{s}\n")
        jobs = [(k, s, os.path.join(kdir, k + ".wav")) for k, s in sents.items() if a.force or not os.path.exists(os.path.join(kdir, k + ".wav"))]
        print(f"{len(sents)} reply sentences, {len(jobs)} to synthesise")
    else:
        os.makedirs(OUT, exist_ok=True)
        jobs = story_jobs(a.force, only)
        print(f"{len(jobs)} story lines to synthesise with {VOICE}")
    sem = asyncio.Semaphore(4); tmp = tempfile.mkdtemp()
    mp3s = [os.path.join(tmp, f"{i}.mp3") for i in range(len(jobs))]
    ok = await asyncio.gather(*[tts(sem, t, m) for (_, t, _), m in zip(jobs, mp3s)])
    made = 0; secs = 0.0
    for (i, t, w), m, good in zip(jobs, mp3s, ok):
        if not good: continue
        d = to_wav(m, w); os.remove(m); made += 1; secs += d
        if not a.kb: print(f"  {i:<22} {d:5.1f}s  {t}")
    print(f"VO summary: {made} generated ({secs:.0f} s of audio), {len(jobs) - made} failed.")
    sys.exit(0 if made == len(jobs) else 1)


if __name__ == "__main__":
    asyncio.run(main())
