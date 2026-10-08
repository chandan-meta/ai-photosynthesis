#!/bin/bash
# Full evidence suite on the current player: beat shots, autoplay (normal + wrong-first), UI shots, intro/finale filmstrips, perf run.
P="D:/AI unity project/AI PHOTOSYNTHESIS"; EXE="$P/Builds/Win/Photosynthesis.exe"; L="$P/Logs"; S="$(dirname "$0")"
run() { log="$1"; shift; "$EXE" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -logFile "$L/$log" "$@"; echo "== $log exit=$?"; grep -E "complete, errors=|quitafter reached|Exception|NullReference" "$L/$log" | sort | uniq -c | head -8; }
cd "$P"
rm -rf "$L/shots" "$L/uishots" "$L/film_intro" "$L/film_finale" "$L/filmsheets_intro" "$L/filmsheets_finale"
run player-film-intro.log -beat Scene2Start -film "$L/film_intro" -quitafter 105
run player-film-finale.log -beat FinalPlant -autoplay -realvoice -film "$L/film_finale" -quitafter 70
run player-shots.log -shots "$L/shots"
run player-autoplay.log -autoplay
run player-autoplay-wrong.log -autoplay -wrongfirst
run player-uishots.log -uishots "$L/uishots"
run player-perf.log -autoplay -perf
python "$S/film_sheets.py" "$L/film_intro" "$L/filmsheets_intro" 4 3 480 2>/dev/null
python "$S/film_sheets.py" "$L/film_finale" "$L/filmsheets_finale" 4 3 480 2>/dev/null
ls "$L/shots" | wc -l; ls "$L/uishots" | wc -l; ls "$L/film_intro" | wc -l; ls "$L/film_finale" | wc -l
grep "\[Perf\]" "$L/player-perf.log" | awk '{b=$2; fps[b]+=$4; n[b]++; dc[b]+=$6; if (min[b]==""||$4<min[b]) min[b]=$4} END {for (b in fps) printf "%-14s fps avg %5.0f min %4d  dc %5.0f\n", b, fps[b]/n[b], min[b], dc[b]/n[b]}' | sort
