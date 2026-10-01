#!/usr/bin/env python3
"""
AMV EDIT 1:1 — genera un video edit a tempo per TikTok (1080x1080).

Uso:
    python make_edit.py [CARTELLA] [opzioni]

Nella CARTELLA (default: quella dello script) servono:
    - la canzone: un file audio (.mp3 .wav .m4a .flac .ogg .aac .opus)
    - clips/      : i video AMV scaricati (mp4, webm, mov, mkv, gif ...)
                    (in alternativa i video possono stare nella cartella stessa)
    - links.txt   : (opzionale) un link Pinterest/altro per riga, scaricati
                    automaticamente in clips/ con yt-dlp
    - testo della canzone (opzionale, in ordine di priorita'):
        lyrics.srt  -> tempi gia' pronti
        lyrics.lrc  -> tempi gia' pronti (formato [mm:ss.xx])
        lyrics.txt  -> solo testo, i tempi vengono trovati con Whisper
      senza nessun file il testo viene trascritto da Whisper (se installato)

Output: CARTELLA/output/edit_1x1.mp4  +  output/lyrics_timed.srt
(correggi lyrics_timed.srt, rinominalo lyrics.srt e rilancia per sistemare il testo)
"""
import argparse
import difflib
import json
import math
import random
import re
import shutil
import subprocess
import sys
from pathlib import Path

AUDIO_EXT = {".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac", ".opus", ".wma"}
VIDEO_EXT = {".mp4", ".webm", ".mov", ".mkv", ".avi", ".gif", ".m4v", ".wmv"}


# ---------------------------------------------------------------- utilita'
def log(*a):
    print("[edit]", *a, flush=True)


def run(cmd, cwd=None, quiet=True):
    r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if r.returncode != 0:
        sys.stderr.write(r.stderr[-3000:])
        raise RuntimeError("comando fallito: " + " ".join(map(str, cmd[:6])) + " ...")
    return r


def need(tool):
    if not shutil.which(tool):
        sys.exit(f"ERRORE: '{tool}' non trovato. Installa FFmpeg (vedi README / installa_requisiti.bat).")


def probe_duration(path):
    r = run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
             "-of", "default=nw=1:nk=1", str(path)])
    try:
        return float(r.stdout.strip())
    except ValueError:
        return 0.0


# ---------------------------------------------------------------- input
def find_inputs(folder):
    audio = [p for p in sorted(folder.iterdir()) if p.suffix.lower() in AUDIO_EXT]
    if not audio:
        sys.exit(f"ERRORE: nessun file audio (la canzone) in {folder}")
    song = audio[0]
    if len(audio) > 1:
        log("piu' file audio trovati, uso:", song.name, "(usa --song per sceglierne un altro)")
    clips_dir = folder / "clips"
    clips = []
    if clips_dir.is_dir():
        clips = [p for p in sorted(clips_dir.rglob("*")) if p.suffix.lower() in VIDEO_EXT]
    clips += [p for p in sorted(folder.iterdir()) if p.suffix.lower() in VIDEO_EXT]
    return song, clips


def download_links(folder):
    links = folder / "links.txt"
    if not links.exists():
        return
    urls = [l.strip() for l in links.read_text(encoding="utf-8", errors="ignore").splitlines()
            if l.strip() and not l.strip().startswith("#")]
    if not urls:
        return
    try:
        import yt_dlp
    except ImportError:
        log("links.txt trovato ma yt-dlp non e' installato (pip install yt-dlp): salto il download")
        return
    out = folder / "clips"
    out.mkdir(exist_ok=True)
    done_file = out / ".scaricati.txt"
    done = set(done_file.read_text().splitlines()) if done_file.exists() else set()
    todo = [u for u in urls if u not in done]
    if not todo:
        return
    log(f"scarico {len(todo)} clip da links.txt ...")
    opts = {"outtmpl": str(out / "%(id)s.%(ext)s"), "format": "bv*+ba/b",
            "merge_output_format": "mp4", "quiet": True, "no_warnings": True,
            "ignoreerrors": True}
    with yt_dlp.YoutubeDL(opts) as ydl:
        for u in todo:
            try:
                if ydl.download([u]) == 0:
                    done.add(u)
            except Exception as e:  # un link rotto non deve fermare tutto
                log("  download fallito:", u, e)
    done_file.write_text("\n".join(sorted(done)))


# ---------------------------------------------------------------- audio / beat
def analyze_audio(song, start, dur, args):
    import numpy as np
    import librosa

    y, sr = librosa.load(str(song), sr=22050, mono=True, offset=start, duration=dur)
    total = len(y) / sr
    hop = 512
    onset = librosa.onset.onset_strength(y=y, sr=sr, hop_length=hop)
    tempo, beats = librosa.beat.beat_track(onset_envelope=onset, sr=sr, hop_length=hop)
    tempo = float(np.atleast_1d(tempo)[0])
    beat_t = librosa.frames_to_time(beats, sr=sr, hop_length=hop)
    if len(beat_t) < 4:  # canzone senza batteria: tagli regolari
        beat_t = np.arange(0, total, 0.5)
        tempo = 120.0
    rms = librosa.feature.rms(y=y, hop_length=hop)[0]
    times = librosa.frames_to_time(np.arange(len(rms)), sr=sr, hop_length=hop)

    def norm(a):
        lo, hi = np.percentile(a, 10), np.percentile(a, 95)
        return np.clip((a - lo) / max(hi - lo, 1e-9), 0, 1)

    rms_n = norm(rms)
    ons_n = norm(onset[:len(rms)])

    def at(arr, t0, t1):
        i0 = int(np.searchsorted(times, t0))
        i1 = max(i0 + 1, int(np.searchsorted(times, t1)))
        return float(np.mean(arr[i0:i1]))

    def peak(arr, t, w=0.06):
        i0 = int(np.searchsorted(times, t - w))
        i1 = max(i0 + 1, int(np.searchsorted(times, t + w)))
        return float(np.max(arr[i0:i1]))

    # punti di taglio: piu' energia = tagli piu' frequenti
    b = [0.0] + [float(t) for t in beat_t if 0.15 < t < total - 0.15]
    cuts, i = [0.0], 0
    while i < len(b) - 1:
        e = at(rms_n, b[i], b[min(i + 1, len(b) - 1)])
        if e > args.fast:
            step = 1
        elif e > args.slow:
            step = 2
        else:
            step = 4
        step = max(1, int(round(step * args.cut_mult)))
        i = min(i + step, len(b) - 1)
        if b[i] - cuts[-1] >= args.min_seg:
            cuts.append(b[i])
    if total - cuts[-1] < args.min_seg and len(cuts) > 1:
        cuts.pop()
    cuts.append(total)

    segs, prev_e = [], 0.0
    strong = np.percentile(ons_n, 85)
    for a, c in zip(cuts[:-1], cuts[1:]):
        e = at(rms_n, a, c)
        segs.append({
            "t0": a, "t1": c, "energy": e,
            "punch": a > 0 and peak(ons_n, a) >= strong,
            "flash": a > 0 and e - prev_e > 0.28,   # attacco / drop
            "slow": e < args.slow,                  # parti calme in slow-motion
        })
        prev_e = e
    log(f"tempo ~{tempo:.0f} BPM, {len(segs)} tagli in {total:.1f}s")
    return total, segs


# ---------------------------------------------------------------- clip / scene
def scene_shots(clip, work, thr):
    cache = work.parent / "scene_cache.json"
    db = json.loads(cache.read_text()) if cache.exists() else {}
    key = f"{clip}|{clip.stat().st_size}|{thr}"
    if key not in db:
        dur = probe_duration(clip)
        cuts = []
        if dur > 0:
            r = subprocess.run(
                ["ffmpeg", "-hide_banner", "-nostats", "-i", str(clip), "-an",
                 "-vf", f"scale=320:-2,select='gt(scene,{thr})',showinfo", "-f", "null", "-"],
                capture_output=True, text=True, encoding="utf-8", errors="replace")
            cuts = [float(x) for x in re.findall(r"pts_time:([\d.]+)", r.stderr)]
        db[key] = {"dur": dur, "cuts": cuts}
        cache.write_text(json.dumps(db))
    d = db[key]
    edges = [0.0] + [c for c in d["cuts"] if 0 < c < d["dur"]] + [d["dur"]]
    shots = []
    for s, e in zip(edges[:-1], edges[1:]):
        s2, e2 = s + 0.06, e - 0.06  # stai lontano dai frame di taglio
        if e2 - s2 > 0.2:
            shots.append({"clip": clip, "s": s2, "e": e2, "used": 0})
    return shots


def assign_shots(segs, shots, rng, speed_slow):
    prev_clip = None
    for seg in segs:
        L = seg["t1"] - seg["t0"]
        src_len = L * (speed_slow if seg["slow"] else 1.0)
        cand = [s for s in shots if s["e"] - s["s"] >= src_len]
        if not cand:
            cand = sorted(shots, key=lambda s: s["e"] - s["s"], reverse=True)[:max(3, len(shots) // 5)]
        not_same = [s for s in cand if s["clip"] != prev_clip] or cand
        min_used = min(s["used"] for s in not_same)
        pool = [s for s in not_same if s["used"] == min_used]
        sh = rng.choice(pool)
        sh["used"] += 1
        room = max(0.0, (sh["e"] - sh["s"]) - src_len)
        seg["clip"] = sh["clip"]
        seg["offset"] = sh["s"] + rng.uniform(0, room)
        prev_clip = sh["clip"]


# ---------------------------------------------------------------- render
def grade_filters(args):
    f = []
    if args.normalize > 0:  # uniforma esposizione/colori tra clip diverse
        f.append(f"normalize=blackpt=black:whitept=white:smoothing=12:independence=0:strength={args.normalize}")
    f.append(f"eq=contrast={args.contrast}:saturation={args.saturation}:gamma={args.gamma}")
    if args.lut:
        f.append(f"lut3d=file='{Path(args.lut).as_posix().replace(':', chr(92) + ':')}'")
    if args.sharpen:
        f.append("unsharp=5:5:0.6")
    return f


def render_segments(segs, work, args):
    S, fps = args.size, args.fps
    grade = grade_filters(args)
    files = []
    for n, seg in enumerate(segs):
        f0, f1 = round(seg["t0"] * fps), round(seg["t1"] * fps)
        frames = max(1, f1 - f0)
        z = args.crop_zoom
        vf = []
        if seg["slow"]:
            vf.append(f"setpts={1 / args.slow_speed:.4f}*PTS")
        vf += [f"fps={fps}",
               f"scale={int(S * z)}:{int(S * z)}:force_original_aspect_ratio=increase:flags=lanczos",
               f"crop={S}:{S}", "setsar=1"]
        vf += grade
        if seg["punch"] and args.punch > 0:
            vf.append(f"zoompan=z='1+{args.punch}*exp(-on/3)':d=1:x='iw/2-iw/zoom/2':"
                      f"y='ih/2-ih/zoom/2':s={S}x{S}:fps={fps}")
        if seg["flash"]:
            vf.append(f"fade=t=in:st=0:d={min(0.18, frames / fps * 0.6):.3f}:color=white")
        vf += ["tpad=stop_mode=clone:stop_duration=10", "format=yuv420p"]
        out = work / f"seg_{n:04d}.mp4"
        run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
             "-ss", f"{seg['offset']:.3f}", "-i", str(seg["clip"]), "-an",
             "-vf", ",".join(vf), "-frames:v", str(frames), "-r", str(fps),
             "-c:v", "libx264", "-preset", "veryfast", "-crf", "14", str(out)])
        files.append(out)
        if n % 10 == 0 or n == len(segs) - 1:
            log(f"  segmenti {n + 1}/{len(segs)}")
    lst = work / "concat.txt"
    lst.write_text("".join(f"file '{p.name}'\n" for p in files))
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-f", "concat", "-safe", "0",
         "-i", "concat.txt", "-c", "copy", "video_only.mp4"], cwd=work)
    return work / "video_only.mp4"


# ---------------------------------------------------------------- testo
def ts_to_s(h, m, s, ms):
    return int(h) * 3600 + int(m) * 60 + int(s) + int(ms.ljust(3, "0")[:3]) / 1000


def parse_srt(path):
    txt = path.read_text(encoding="utf-8-sig", errors="ignore")
    out = []
    for block in re.split(r"\n\s*\n", txt.replace("\r", "")):
        m = re.search(r"(\d+):(\d+):(\d+)[,.](\d+)\s*-->\s*(\d+):(\d+):(\d+)[,.](\d+)", block)
        if not m:
            continue
        body = block[m.end():].strip().replace("\n", " ")
        body = re.sub(r"<[^>]+>|\{[^}]*\}", "", body).strip()
        if body:
            out.append({"t0": ts_to_s(*m.groups()[:4]), "t1": ts_to_s(*m.groups()[4:]), "text": body})
    return out


def parse_lrc(path, total_song):
    rows = []
    for line in path.read_text(encoding="utf-8-sig", errors="ignore").splitlines():
        tags = re.findall(r"\[(\d+):(\d+(?:\.\d+)?)\]", line)
        text = re.sub(r"\[[^\]]*\]", "", line).strip()
        for m, s in tags:
            rows.append((int(m) * 60 + float(s), text))
    rows.sort()
    out = []
    for i, (t, text) in enumerate(rows):
        nxt = rows[i + 1][0] if i + 1 < len(rows) else min(t + 5, total_song)
        if text:
            out.append({"t0": t, "t1": min(nxt, t + 7), "text": text})
    return out


def whisper_words(wav, args, prompt=None):
    """Lista di parole (word, start, end) con i tempi, o None se Whisper manca."""
    try:
        from faster_whisper import WhisperModel
        log(f"Whisper (faster-whisper, modello {args.model}) sta ascoltando la canzone ...")
        model = WhisperModel(args.model, device="auto", compute_type="auto")
        segs, _ = model.transcribe(str(wav), language=args.lang, word_timestamps=True,
                                   initial_prompt=prompt, vad_filter=False)
        return [(w.word.strip(), w.start, w.end) for s in segs for w in (s.words or []) if w.word.strip()]
    except ImportError:
        pass
    try:
        import whisper
        log(f"Whisper (openai-whisper, modello {args.model}) sta ascoltando la canzone ...")
        model = whisper.load_model(args.model)
        r = model.transcribe(str(wav), language=args.lang, word_timestamps=True, initial_prompt=prompt)
        return [(w["word"].strip(), w["start"], w["end"]) for s in r["segments"]
                for w in s.get("words", []) if w["word"].strip()]
    except ImportError:
        return None


def tok(w):
    return re.sub(r"[^\w']", "", w.lower())


def words_to_lines(words, max_words=5, gap=0.55):
    lines, cur = [], []
    for w in words:
        if cur and (len(cur) >= max_words or w[1] - cur[-1][2] > gap):
            lines.append(cur)
            cur = []
        cur.append(w)
        if re.search(r"[.!?,;]$", w[0]) and len(cur) >= 2:
            lines.append(cur)
            cur = []
    if cur:
        lines.append(cur)
    return [{"t0": l[0][1], "t1": l[-1][2], "words": [(re.sub(r"[.,;]$", "", w), s, e) for w, s, e in l]}
            for l in lines]


def align_text(lines_txt, words):
    """Allinea le righe di lyrics.txt alle parole (con tempi) trovate da Whisper."""
    tw = [(li, w) for li, line in enumerate(lines_txt) for w in line.split()]
    a = [tok(w) for _, w in tw]
    b = [tok(w[0]) for w in words]
    match = {}
    for blk in difflib.SequenceMatcher(None, a, b, autojunk=False).get_matching_blocks():
        for k in range(blk.size):
            match[blk.a + k] = blk.b + k
    times = [None] * len(tw)
    for i, j in match.items():
        times[i] = (words[j][1], words[j][2])
    # interpola le parole non riconosciute tra quelle note
    known = [i for i, t in enumerate(times) if t]
    if not known:
        return None
    end_all = words[-1][2]
    for i in range(len(tw)):
        if times[i]:
            continue
        p = max((k for k in known if k < i), default=None)
        n = min((k for k in known if k > i), default=None)
        t_lo = times[p][1] if p is not None else max(0.0, times[n][0] - 0.4 * (n - i))
        t_hi = times[n][0] if n is not None else min(end_all + 0.4 * (i - p), end_all + 3)
        i_lo = p if p is not None else i - 1
        i_hi = n if n is not None else i + 1
        f0 = (i - i_lo - 1) / (i_hi - i_lo - 1) if i_hi - i_lo > 1 else 0
        f1 = (i - i_lo) / (i_hi - i_lo - 1) if i_hi - i_lo > 1 else 1
        times[i] = (t_lo + (t_hi - t_lo) * f0, t_lo + (t_hi - t_lo) * min(f1, 1))
    out = []
    for li, line in enumerate(lines_txt):
        ws = [(w, *times[k]) for k, (lj, w) in enumerate(tw) if lj == li]
        if ws:
            out.append({"t0": ws[0][1], "t1": ws[-1][2], "words": ws})
    return out


def even_words(line):
    ws = line["text"].split()
    if not ws:
        return []
    d = (line["t1"] - line["t0"]) / len(ws)
    return [(w, line["t0"] + i * d, line["t0"] + (i + 1) * d) for i, w in enumerate(ws)]


def build_lyrics(folder, song, start, total, work, args):
    srt, lrc, txt = folder / "lyrics.srt", folder / "lyrics.lrc", folder / "lyrics.txt"
    song_total = probe_duration(song)
    lines = None
    if args.no_lyrics:
        return []
    if srt.exists():
        log("testo: lyrics.srt")
        lines = parse_srt(srt)
    elif lrc.exists():
        log("testo: lyrics.lrc")
        lines = parse_lrc(lrc, song_total)
    if lines is not None:
        for l in lines:
            l["t0"] -= start
            l["t1"] -= start
            l["words"] = even_words(l)
        lines = [l for l in lines if l["t1"] > 0 and l["t0"] < total]
    else:
        wav = work / "voice.wav"
        run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-ss", str(start), "-t", str(total),
             "-i", str(song), "-ac", "1", "-ar", "16000", str(wav)])
        txt_lines = None
        if txt.exists():
            txt_lines = [l.strip() for l in txt.read_text(encoding="utf-8-sig", errors="ignore").splitlines()
                         if l.strip() and not re.match(r"^\[.*\]$", l.strip())]
        prompt = " ".join(txt_lines)[:800] if txt_lines else None
        words = whisper_words(wav, args, prompt)
        if words is None:
            log("ATTENZIONE: Whisper non installato e nessun lyrics.srt/.lrc -> video senza testo")
            log("  installa con:  pip install faster-whisper")
            return []
        if not words:
            log("Whisper non ha trovato parole: video senza testo")
            return []
        lines = align_text(txt_lines, words) if txt_lines else None
        if lines is None:
            lines = words_to_lines(words)
    # durata minima/massima di ogni riga e niente sovrapposizioni
    lines.sort(key=lambda l: l["t0"])
    for i, l in enumerate(lines):
        nxt = lines[i + 1]["t0"] if i + 1 < len(lines) else total
        l["t1"] = min(max(l["t1"] + 0.35, l["t0"] + 0.8), nxt - 0.02, total)
    return [l for l in lines if l["t1"] - l["t0"] > 0.1]


def fmt_srt(t):
    t = max(0, t)
    return f"{int(t // 3600):02d}:{int(t % 3600 // 60):02d}:{int(t % 60):02d},{int(round(t % 1 * 1000)) % 1000:03d}"


def fmt_ass(t):
    t = max(0, t)
    cs = int(round(t * 100))
    return f"{cs // 360000}:{cs // 6000 % 60:02d}:{cs // 100 % 60:02d}.{cs % 100:02d}"


def ass_color(hexrgb, alpha=0):
    h = hexrgb.lstrip("#")
    return f"&H{alpha:02X}{h[4:6]}{h[2:4]}{h[0:2]}".upper()


def write_subs(lines, start, work, out_dir, args):
    # .srt con i tempi della canzone intera: si puo' correggere e riusare come lyrics.srt
    with open(out_dir / "lyrics_timed.srt", "w", encoding="utf-8") as f:
        for i, l in enumerate(lines, 1):
            text = " ".join(w for w, _, _ in l["words"])
            f.write(f"{i}\n{fmt_srt(l['t0'] + start)} --> {fmt_srt(l['t1'] + start)}\n{text}\n\n")
    S = args.size
    fs = int(S * args.font_scale)
    head = f"""[Script Info]
ScriptType: v4.00+
PlayResX: {S}
PlayResY: {S}
WrapStyle: 0
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Lyric,{args.font},{fs},{ass_color(args.color)},{ass_color('#FFFFFF', 0x9A)},{ass_color('#000000')},{ass_color('#000000', 0x60)},-1,0,0,0,100,100,1,0,1,{max(2, fs // 14)},{max(1, fs // 22)},2,70,70,{int(S * args.text_y)},1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
    ev = []
    for l in lines:
        lead = 0.12
        t0 = max(0.0, l["t0"] - lead)
        parts = [f"{{\\k{int(round((l['words'][0][1] - t0) * 100))}}}"]
        for k, (w, s, e) in enumerate(l["words"]):
            nxt = l["words"][k + 1][1] if k + 1 < len(l["words"]) else max(e, s + 0.05)
            w = w.replace("{", "").replace("}", "").replace("\\", "")
            if args.upper:
                w = w.upper()
            parts.append(f"{{\\kf{max(1, int(round((nxt - s) * 100)))}}}{w} ")
        anim = "{\\fad(90,120)\\fscx82\\fscy82\\t(0,140,\\fscx100\\fscy100)}"
        ev.append(f"Dialogue: 0,{fmt_ass(t0)},{fmt_ass(l['t1'])},Lyric,,0,0,0,,{anim}{''.join(parts).rstrip()}")
    (work / "lyrics.ass").write_text(head + "\n".join(ev) + "\n", encoding="utf-8")


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description="Genera un AMV edit 1:1 a tempo con il testo.")
    ap.add_argument("folder", nargs="?", default=str(Path(__file__).resolve().parent))
    ap.add_argument("--song", help="file audio da usare (default: il primo trovato)")
    ap.add_argument("--start", type=float, default=0.0, help="secondo della canzone da cui partire")
    ap.add_argument("--dur", type=float, default=0.0, help="durata in secondi (0 = fino alla fine)")
    ap.add_argument("--out", default="edit_1x1.mp4")
    ap.add_argument("--size", type=int, default=1080)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--seed", type=int, default=7, help="cambia per un montaggio diverso")
    # ritmo dei tagli
    ap.add_argument("--fast", type=float, default=0.62, help="energia sopra cui si taglia ad ogni beat")
    ap.add_argument("--slow", type=float, default=0.25, help="energia sotto cui si taglia ogni 4 beat (+slow-mo)")
    ap.add_argument("--cut-mult", type=float, default=1.0, help="2 = tagli meno frequenti, 0.5 = piu' frequenti")
    ap.add_argument("--min-seg", type=float, default=0.22)
    ap.add_argument("--slow-speed", type=float, default=0.8, help="velocita' nelle parti calme")
    ap.add_argument("--scene-thr", type=float, default=0.32, help="sensibilita' rilevamento scene nelle clip")
    # look
    ap.add_argument("--crop-zoom", type=float, default=1.04, help=">1 taglia di piu' i bordi (watermark)")
    ap.add_argument("--punch", type=float, default=0.10, help="zoom sui colpi forti (0 = off)")
    ap.add_argument("--contrast", type=float, default=1.08)
    ap.add_argument("--saturation", type=float, default=1.18)
    ap.add_argument("--gamma", type=float, default=0.97)
    ap.add_argument("--normalize", type=float, default=0.35, help="uniformita' colori tra clip (0-1)")
    ap.add_argument("--lut", help="file .cube per un color grading personalizzato")
    ap.add_argument("--sharpen", action="store_true")
    ap.add_argument("--no-vignette", action="store_true")
    # testo
    ap.add_argument("--no-lyrics", action="store_true")
    ap.add_argument("--model", default="small", help="modello Whisper: tiny/base/small/medium/large-v3")
    ap.add_argument("--lang", default=None, help="lingua della canzone, es. it / en (default: auto)")
    ap.add_argument("--font", default="Arial Black")
    ap.add_argument("--font-scale", type=float, default=0.062)
    ap.add_argument("--color", default="#FFFFFF", help="colore delle parole cantate")
    ap.add_argument("--text-y", type=float, default=0.12, help="distanza del testo dal fondo (frazione)")
    ap.add_argument("--no-upper", dest="upper", action="store_false")
    ap.add_argument("--keep", action="store_true", help="non cancellare i file temporanei")
    args = ap.parse_args()

    need("ffmpeg")
    need("ffprobe")
    folder = Path(args.folder).expanduser().resolve()
    if not folder.is_dir():
        sys.exit(f"ERRORE: cartella non trovata: {folder}")
    download_links(folder)
    song, clips = find_inputs(folder)
    if args.song:
        song = Path(args.song) if Path(args.song).is_absolute() else folder / args.song
    if not clips:
        sys.exit(f"ERRORE: nessuna clip video. Mettile in {folder / 'clips'} oppure scrivi i link in links.txt")
    log("canzone:", song.name, "| clip:", len(clips))

    out_dir = folder / "output"
    work = out_dir / "_work"
    work.mkdir(parents=True, exist_ok=True)
    for old in work.glob("seg_*.mp4"):
        old.unlink()

    song_len = probe_duration(song)
    dur = args.dur if args.dur > 0 else song_len - args.start
    dur = min(dur, song_len - args.start)
    total, segs = analyze_audio(song, args.start, dur, args)

    log("analizzo le scene delle clip ...")
    shots = []
    for c in clips:
        try:
            shots += scene_shots(c, work, args.scene_thr)
        except Exception as e:
            log("  clip ignorata:", c.name, e)
    if not shots:
        sys.exit("ERRORE: nessuna clip leggibile")
    log(f"{len(shots)} inquadrature disponibili")
    rng = random.Random(args.seed)
    assign_shots(segs, shots, rng, args.slow_speed)

    log("monto i segmenti ...")
    render_segments(segs, work, args)

    lines = build_lyrics(folder, song, args.start, total, work, args)
    write_subs(lines, args.start, work, out_dir, args)
    log(f"righe di testo: {len(lines)}")

    frames = round(total * args.fps)
    vdur = frames / args.fps
    vf = ["ass=lyrics.ass"]
    if not args.no_vignette:
        vf.insert(0, "vignette=angle=PI/5")
    af = [f"afade=t=out:st={max(0, vdur - 0.6):.3f}:d=0.6"]
    if args.start > 0:
        af.insert(0, "afade=t=in:st=0:d=0.25")
    final = out_dir / args.out
    log("render finale ...")
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
         "-i", "video_only.mp4", "-ss", f"{args.start:.3f}", "-t", f"{vdur:.3f}", "-i", str(song),
         "-map", "0:v", "-map", "1:a", "-vf", ",".join(vf), "-af", ",".join(af),
         "-frames:v", str(frames), "-c:v", "libx264", "-preset", "medium", "-crf", "17",
         "-profile:v", "high", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-ar", "44100",
         "-shortest", "-movflags", "+faststart", str(final.resolve())], cwd=work)
    if not args.keep:
        shutil.rmtree(work, ignore_errors=True)
    log("FATTO ->", final)
    log("testo con i tempi ->", out_dir / "lyrics_timed.srt")


if __name__ == "__main__":
    main()
