# Record what the default output device plays (WASAPI loopback) to a WAV until a stop file appears or max seconds pass.
# Resilient: if the stream goes inactive (device idle/disconnect/default change) it is reopened on the current default
# device, and the gap is filled with silence so the file stays in sync with wall-clock time.
import sys, os, time, wave, threading
import pyaudiowpatch as pa
out, stop_file, max_s = sys.argv[1], sys.argv[2], float(sys.argv[3])
RATE, CH = 48000, 2
wf = wave.open(out, "wb"); wf.setnchannels(CH); wf.setsampwidth(2); wf.setframerate(RATE)
lock = threading.Lock(); written = 0   # frames written so far

def cb(data, n, t, status):
    global written
    with lock: wf.writeframes(data); written += n
    return (data, pa.paContinue)

def open_stream(p):
    w = p.get_host_api_info_by_type(pa.paWASAPI); d = p.get_device_info_by_index(w["defaultOutputDevice"])
    lb = next(x for x in p.get_loopback_device_info_generator() if d["name"] in x["name"])
    st = p.open(format=pa.paInt16, channels=CH, rate=RATE, input=True, input_device_index=lb["index"], frames_per_buffer=1024, stream_callback=cb)
    return st, lb["name"]

p = pa.PyAudio(); t0 = time.time(); st = None; name = None
print(f"[rec] audio start {t0:.3f}", flush=True)
while not os.path.exists(stop_file) and time.time() - t0 < max_s:
    if st is None or not st.is_active():
        if st is not None:
            try: st.close()
            except Exception: pass
            st = None; print(f"[rec] stream dropped at {time.time() - t0:.1f} s", flush=True)
        try:
            st, name = open_stream(p)
            with lock:   # pad the gap so audio time == wall time
                want = int((time.time() - t0) * RATE)
                if want > written: wf.writeframes(b"\0" * ((want - written) * CH * 2)); written = want
            print(f"[rec] stream open on {name} at {time.time() - t0:.1f} s", flush=True)
        except Exception as e:
            st = None; time.sleep(0.5); continue
    time.sleep(0.2)
if st is not None:
    try: st.stop_stream(); st.close()
    except Exception: pass
wf.close(); p.terminate()
print(f"[rec] audio stop {time.time():.3f} ({time.time() - t0:.1f} s, {written / RATE:.1f} s written)", flush=True)
