#!/bin/bash
# postprocess.sh <master.mp4> — verify, make a 720p copy for sending, extract check frames + a contact sheet
IN="$1"; S="$(cd "$(dirname "$0")" && pwd)"
FF7="C:/Users/ADMIN/AppData/Local/Programs/Python/Python314/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe"
OUT720="${IN%.mp4}_720p.mp4"; F="$(dirname "$IN")/frames"; rm -rf "$F"; mkdir -p "$F"
DUR=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$IN"); echo "master: $(ls -la --block-size=M "$IN" | awk '{print $5}') $(printf '%.0f' "$DUR") s"
ffprobe -v error -select_streams a -show_entries stream=codec_name,channels,sample_rate -of csv=p=0 "$IN"
ffmpeg -hide_banner -i "$IN" -af "volumedetect" -vn -f null - 2>&1 | grep "mean_volume\|max_volume"
# silence check per minute: where does the audio go quiet for a whole minute?
ffmpeg -hide_banner -i "$IN" -af "silencedetect=noise=-45dB:d=20" -vn -f null - 2>&1 | grep "silence_start\|silence_end" | head -10
"$FF7" -hide_banner -loglevel error -y -i "$IN" -vf "scale=1280:-2" -c:v h264_nvenc -preset p5 -rc vbr -cq 26 -b:v 0 -c:a copy -movflags +faststart "$OUT720"
echo "720p: $(ls -la --block-size=M "$OUT720" | awk '{print $5}')"
ffmpeg -hide_banner -loglevel error -y -i "$IN" -vf "fps=1/30,scale=480:-1,tile=5x6" -frames:v 1 "$F/sheet.jpg"
echo "sheet: $F/sheet.jpg"
