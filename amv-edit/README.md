# AMV edit 1:1 per TikTok

`make_edit.py` monta un video edit **1080×1080** sulla tua canzone: taglia le clip AMV
sui beat, uniforma i colori, aggiunge zoom/flash sui colpi forti e il testo a tempo (karaoke).

## Setup (una volta, su Windows)
1. Installa Python 3.10+ da python.org (spunta "Add Python to PATH").
2. Copia `make_edit.py`, `crea_edit.bat`, `installa_requisiti.bat` in `C:\Users\Amethyst\Downloads\edit`.
3. Doppio clic su `installa_requisiti.bat` (FFmpeg, librosa, faster-whisper, yt-dlp).

## Cartella `edit`
```
edit\
  la_mia_canzone.mp3      <- la canzone
  clips\                  <- i video AMV (mp4/webm/mov/gif...)
  links.txt               <- (opzionale) link Pinterest, uno per riga: scaricati in clips\
  lyrics.txt              <- (consigliato) il testo, una riga per verso
```
Il testo: `lyrics.srt` o `lyrics.lrc` (tempi pronti) > `lyrics.txt` (tempi trovati da Whisper)
> niente (Whisper trascrive da solo).

## Avvio
Doppio clic su `crea_edit.bat` → risultato in `edit\output\edit_1x1.mp4`.

Per correggere il testo: modifica `output\lyrics_timed.srt`, salvalo come `edit\lyrics.srt`, rilancia.

## Opzioni utili (da terminale: `python make_edit.py . --opzione`)
| opzione | effetto |
|---|---|
| `--start 45 --dur 30` | usa solo 30 s dal secondo 45 (ritornello) |
| `--seed 3` | montaggio diverso con le stesse clip |
| `--cut-mult 0.5` / `2` | tagli più / meno frequenti |
| `--lang it` `--model medium` | Whisper più preciso |
| `--font "Impact" --color "#FF4FD8"` | stile del testo |
| `--crop-zoom 1.15` | taglia di più i bordi (watermark) |
| `--lut look.cube` | color grading personalizzato |
| `--punch 0` | niente zoom sui beat |
