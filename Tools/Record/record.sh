#!/bin/bash
# record.sh <out.mp4> <max seconds> [player args...]
# Release player (windowed 1600x900) + NVENC capture of the window's screen region + WASAPI loopback audio, muxed to mp4.
S="$(cd "$(dirname "$0")" && pwd)"; P="$(cd "$S/../.." && pwd)"
OUT="$1"; MAX="$2"; shift 2; W="$P/Recordings/work"; rm -rf "$W"; mkdir -p "$W"; STOP="$W/stop"
WIDTH=1600; HEIGHT=900
FF="${FF:-ffmpeg}"; ENC="${ENC:--c:v libx264 -preset veryfast -crf 20}"   # override: FF=<ffmpeg exe> ENC="-c:v h264_nvenc ..."
python -u "$S/loopback_rec.py" "$W/audio.wav" "$STOP" "$MAX" > "$W/audio.log" 2>&1 &
APID=$!
"$P/Builds/WinRelease/Photosynthesis.exe" -screen-width $WIDTH -screen-height $HEIGHT -screen-fullscreen 0 -logFile "$W/player.log" "$@" &
GPID=$!
RECT=""
for i in $(seq 1 80); do sleep 0.5; RECT=$(powershell -NoProfile -ExecutionPolicy Bypass -File "$S/winrect.ps1"); case "$RECT" in *"client=${WIDTH}x${HEIGHT} at="*) break;; esac; RECT=""; done
if [ -z "$RECT" ]; then echo "window never reached ${WIDTH}x${HEIGHT}"; taskkill //F //IM Photosynthesis.exe >/dev/null 2>&1; touch "$STOP"; exit 1; fi
X=${RECT##*at=}; Y=${X#*,}; X=${X%,*}; echo "capture region ${WIDTH}x${HEIGHT} at $X,$Y"
sleep 0.5; VSTART=$(date +%s.%N)
"$FF" -hide_banner -loglevel error -y -f gdigrab -framerate 30 -draw_mouse 0 -offset_x "$X" -offset_y "$Y" -video_size ${WIDTH}x${HEIGHT} -i desktop -t "$MAX" $ENC -pix_fmt yuv420p "$W/video.mkv" > "$W/ffmpeg.log" 2>&1 &
FPID=$!
wait $GPID; echo "player exited $(date +%s.%N)"; sleep 1; touch "$STOP"; wait $APID
kill -INT $FPID 2>/dev/null; sleep 2; taskkill //F //IM ffmpeg.exe >/dev/null 2>&1; taskkill //F //IM ffmpeg-win-x86_64-v7.1.exe >/dev/null 2>&1; sleep 1
ASTART=$(grep -o "audio start [0-9.]*" "$W/audio.log" | awk '{print $3}')
OFF=$(python -c "print(max(0.0, $VSTART + 0.35 - $ASTART))")   # capture begins ~0.35 s after the ffmpeg launch
echo "audio start $ASTART, video start ~$VSTART, trim audio by $OFF s"
ffmpeg -hide_banner -loglevel error -y -i "$W/video.mkv" -ss "$OFF" -i "$W/audio.wav" -map 0:v -map 1:a -c:v copy -c:a aac -b:a 160k -shortest -movflags +faststart "$OUT"
ffprobe -hide_banner -v error -show_entries format=duration:stream=codec_name,width,height,avg_frame_rate -of csv=p=0 "$OUT"; ls -la --block-size=M "$OUT" | awk '{print $5}'
grep -a "autoplay complete\|quitafter reached\|Exception" "$W/player.log" | head -3; head -3 "$W/ffmpeg.log"
