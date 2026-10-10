"""Validate official downloads, measure loudness, and prepare separate Unity playback copies."""
import hashlib, json, re, subprocess
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets/Photosynthesis/Audio'
OUT = ART / 'Resources/Soundtrack'
OUT.mkdir(parents=True, exist_ok=True)
LOG = ROOT / 'Logs/AudioUpgrade'
LOG.mkdir(parents=True, exist_ok=True)
tracks = [
 ('Dreamy Flashback', 'Dreamy Flashback.mp3', 'BGM_Cinematic_Intro', 'USUAN1100532'),
 ('Light Thought var 2', 'Light Thought var 2.mp3', 'BGM_Main_Exploration', 'USUAN1200007'),
 ('Silver Blue Light', 'Silver Blue Light.mp3', 'BGM_Root_Water', 'USUAN1100718'),
 ('Cipher', 'Cipher2.mp3', 'BGM_Scientific_Discovery', 'USUAN1100844'),
 ('Carefree', 'Carefree.mp3', 'BGM_DrCurio_Playful', 'USUAN1400037'),
 ('Heartwarming', 'Heartwarming.mp3', 'BGM_Completion', 'USUAN1100207'),
 ('Forest Ambience', 'Forest_Ambience.mp3', 'AMB_Forest_Nature', None)]
def run(args):
    return subprocess.run(args, check=True, capture_output=True, text=True)
def measure(path):
    r=run(['ffmpeg','-hide_banner','-i',str(path),'-af','loudnorm=I=-20:TP=-1.5:LRA=11:print_format=json','-f','null','-'])
    return json.loads(re.findall(r'\{[^{}]+\}',r.stderr)[-1])
records=[]
for title, filename, target, isrc in tracks:
    source=ART/'Originals'/filename
    info=json.loads(run(['ffprobe','-v','error','-show_format','-show_streams','-of','json',str(source)]).stdout)
    stream=next(s for s in info['streams'] if s['codec_type']=='audio')
    assert stream['codec_name']=='mp3' and float(info['format']['duration'])>20, f'Invalid download: {filename}'
    before=measure(source)
    target_lufs = -24 if isrc is None else -20
    gain=max(-18,min(36,target_lufs-float(before['input_i'])))
    # Attenuation-only peak safety if integrated loudness and transient level cannot both meet targets.
    gain=min(gain,-1.5-float(before['input_tp']))
    loop = isrc is not None and target!='BGM_Completion'
    if loop:
        # Carry the tail into the head; rotate the start so the playback seam joins contiguous source samples.
        graph=f'[0:a]volume={gain}dB,asplit=2[a][b];[a]atrim=start=2,asetpts=PTS-STARTPTS[body];[b]atrim=end=2,asetpts=PTS-STARTPTS[head];[body][head]acrossfade=d=2:c1=tri:c2=tri[out]'
        args=['-filter_complex',graph,'-map','[out]']
    else: args=['-af',f'volume={gain}dB']
    output=OUT/(target+'.ogg')
    run(['ffmpeg','-y','-hide_banner','-i',str(source),'-vn','-sn','-dn',*args,'-ar','44100','-c:a','libvorbis','-q:a','6',str(output)])
    after=measure(output)
    pcm=subprocess.run(['ffmpeg','-v','error','-i',str(output),'-f','f32le','-ac','2','-ar','44100','-'],check=True,capture_output=True).stdout
    import numpy as np
    samples=np.frombuffer(pcm,dtype='<f4').reshape(-1,2)
    peak=float(np.abs(samples).max()); seam=float(np.abs(samples[0]-samples[-1]).max())
    assert peak<1 and len(samples)>44100*20
    records.append(dict(title=title,isrc=isrc,original=str(source.relative_to(ROOT)),asset=str(output.relative_to(ROOT)),
        sha256=hashlib.sha256(source.read_bytes()).hexdigest(),source_format=stream,
        original_lufs=float(before['input_i']),gain_db=gain,prepared_lufs=float(after['input_i']),
        prepared_true_peak_db=float(after['input_tp']),sample_peak=peak,seam_jump=seam,
        duration=len(samples)/44100,loop_overlap_seconds=2 if loop else 0,
        source_url='https://incompetech.com/music/royalty-free/index.html?isrc='+isrc if isrc else 'https://opengameart.org/content/forest-ambience',
        license='CC BY 4.0' if isrc else 'CC0 1.0'))
    print(title, 'validated; LUFS',after['input_i'],'peak',after['input_tp'],'seam',round(seam,6),flush=True)
(ART/'soundtrack_manifest.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
(LOG/'download-verification.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
