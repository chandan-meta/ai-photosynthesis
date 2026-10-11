mergeInto(LibraryManager.library, {
  $CurioBridge: {
    current: null,
    device: '',
    micError: function(error) {
      if (error.name === 'NotAllowedError') return 'Microphone access blocked. Allow the microphone for localhost in your browser, then retry.';
      if (error.name === 'NotFoundError') return 'No microphone found. Connect a microphone and check Windows Sound input settings.';
      if (error.name === 'NotReadableError') return 'Microphone is busy or unavailable. Close other recording apps and check Windows microphone privacy.';
      return 'Could not open the microphone (' + error.name + '). Choose another microphone in Settings or type your question.';
    },
    microphones: async function(target) {
      try {
        var stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        stream.getTracks().forEach(function(t) { t.stop(); });
        var devices = (await navigator.mediaDevices.enumerateDevices()).filter(function(d) { return d.kind === 'audioinput'; });
        SendMessage(target, 'OnBrowserMicrophones', JSON.stringify({ devices: devices.map(function(d, i) { return { id: d.deviceId, label: d.label || 'Microphone ' + (i + 1) }; }), error: '' }));
      } catch (error) { SendMessage(target, 'OnBrowserMicrophones', JSON.stringify({ devices: [], error: CurioBridge.micError(error) })); }
    },
    emit: function(s, type, data) {
      if (CurioBridge.current !== s) return;
      SendMessage(s.target, 'OnCurioEvent', JSON.stringify(Object.assign({ type: type }, data || {})));
    },
    send: function(s, event) {
      if (s.dc && s.dc.readyState === 'open') s.dc.send(JSON.stringify(event));
    },
    stop: function(s, notify) {
      if (!s || s.closed) return;
      s.closed = true; s.abort.abort();
      clearInterval(s.meter); clearTimeout(s.timeout); clearTimeout(s.limit);
      clearTimeout(s.micRelease);
      clearTimeout(s.micLimit);
      if (s.ws) s.ws.close();
      if (s.processor) s.processor.disconnect();
      if (s.sources) s.sources.forEach(function(source) { try { source.stop(); } catch (_) {} });
      if (s.stream) s.stream.getTracks().forEach(function(t) { t.stop(); });
      if (s.audio) { s.audio.pause(); s.audio.srcObject = null; }
      if (s.pc) s.pc.close();
      if (s.ac) s.ac.close().catch(function() {});
      document.removeEventListener('visibilitychange', s.visibility);
      window.removeEventListener('pagehide', s.pagehide);
      window.removeEventListener('pointerdown', s.resume);
      if (s.ticket) fetch(s.url + '/close', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ticket: s.ticket }), keepalive: true }).catch(function() {});
      if (notify) CurioBridge.emit(s, 'closed');
      if (CurioBridge.current === s) CurioBridge.current = null;
    },
    fail: function(s) { CurioBridge.emit(s, 'error'); CurioBridge.stop(s, false); },
    interrupt: function(s) {
      if (s.gemini) {
        (s.sources || []).forEach(function(source) { try { source.stop(); } catch (_) {} });
        s.sources = []; s.nextAudio = 0; s.playing = false;
        CurioBridge.emit(s, 'stopped'); return;
      }
      s.ignore = s.response;
      if (s.response) CurioBridge.send(s, { type: 'response.cancel' });
      if (s.playing) CurioBridge.send(s, { type: 'output_audio_buffer.clear' });
      s.playing = false;
      CurioBridge.emit(s, 'stopped');
    },
    ask: function(s, text) {
      if (s.gemini) {
        CurioBridge.interrupt(s); s.text = ''; s.question = ''; s.turnDone = false;
        s.ws.send(JSON.stringify({ type: 'ask', text: text.slice(0, 500) })); CurioBridge.emit(s, 'thinking'); return;
      }
      s.pending = text; CurioBridge.interrupt(s);
      if (!s.response) CurioBridge.flush(s);
    },
    flush: function(s) {
      if (!s.pending || s.closed) return;
      var text = s.pending; s.pending = null; s.started = performance.now(); s.first = 0;
      CurioBridge.send(s, { type: 'conversation.item.create', item: { type: 'message', role: 'user', content: [{ type: 'input_text', text: text.slice(0, 500) }] } });
      CurioBridge.send(s, { type: 'response.create' });
      CurioBridge.emit(s, 'thinking');
    },
    mic: async function(s, on) {
      if (s.gemini) { await CurioBridge.geminiMic(s, on); return; }
      s.micWanted = on;
      if (!on) { if (s.stream) s.stream.getAudioTracks().forEach(function(t) { t.enabled = false; }); return; }
      CurioBridge.interrupt(s);
      s.started = performance.now(); s.first = 0;
      try {
        await s.ac.resume();
        if (!s.stream) {
          if (s.gettingMic) return;
          s.gettingMic = true;
          var stream;
          try { stream = await navigator.mediaDevices.getUserMedia({ audio: { deviceId: CurioBridge.device ? { exact: CurioBridge.device } : undefined, echoCancellation: true, noiseSuppression: true, autoGainControl: true } }); }
          finally { s.gettingMic = false; }
          if (s.closed) { stream.getTracks().forEach(function(t) { t.stop(); }); return; }
          s.stream = stream;
          s.micAnalyser = s.ac.createAnalyser(); s.micAnalyser.fftSize = 256;
          s.ac.createMediaStreamSource(stream).connect(s.micAnalyser);
          await s.sender.replaceTrack(stream.getAudioTracks()[0]);
        }
        if (s.closed) return;
        s.stream.getAudioTracks().forEach(function(t) { t.enabled = s.micWanted; });
        CurioBridge.emit(s, s.micWanted ? 'listening' : 'stopped');
        if (!s.micWanted) CurioBridge.emit(s, 'notice', { text: 'Microphone ready. Hold it again to ask your question.' });
      } catch (error) { s.micWanted = false; CurioBridge.emit(s, 'stopped'); CurioBridge.emit(s, 'notice', { text: CurioBridge.micError(error) }); }
    },
    event: function(s, e) {
      if (s.closed) return;
      if (e.event_id && s.seen.has(e.event_id)) return;
      if (e.event_id) { s.seen.add(e.event_id); if (s.seen.size > 512) s.seen.delete(s.seen.values().next().value); }
      if (e.type === 'response.created') { s.response = e.response.id; s.text = ''; CurioBridge.emit(s, 'thinking'); }
      if (e.type === 'input_audio_buffer.speech_started') { CurioBridge.interrupt(s); s.started = performance.now(); s.first = 0; CurioBridge.emit(s, 'listening'); }
      if (e.type === 'input_audio_buffer.speech_stopped') { s.started = performance.now(); CurioBridge.emit(s, 'thinking'); }
      if (e.type === 'conversation.item.input_audio_transcription.completed') CurioBridge.emit(s, 'question', { text: e.transcript });
      if (e.type === 'conversation.item.input_audio_transcription.failed') CurioBridge.emit(s, 'notice', { text: 'I could not transcribe that. Please try again or type it.' });
      if (e.response_id && e.response_id === s.ignore && e.type !== 'response.done') return;
      if (e.type === 'response.output_audio_transcript.delta' || e.type === 'response.output_text.delta') {
        s.text = (s.text + e.delta).slice(0, 4000); CurioBridge.emit(s, 'delta', { text: s.text });
      }
      if (e.type === 'response.output_audio_transcript.done' || e.type === 'response.output_text.done') CurioBridge.emit(s, 'answer', { text: e.transcript || e.text || s.text });
      if (e.type === 'output_audio_buffer.started') { s.playing = true; CurioBridge.emit(s, 'speaking'); }
      if (e.type === 'output_audio_buffer.stopped' || e.type === 'output_audio_buffer.cleared') {
        s.playing = false; CurioBridge.emit(s, 'stopped');
        if (s.first) CurioBridge.emit(s, 'latency', { firstAudioMs: s.first, totalMs: performance.now() - s.started });
      }
      if (e.type === 'response.done') {
        s.response = null;
        if (e.response.status === 'failed') CurioBridge.emit(s, 'notice', { text: 'That answer could not finish. Please try again.' });
        if (!s.playing) CurioBridge.emit(s, 'stopped');
        CurioBridge.flush(s);
      }
      if (e.type === 'error' && !['response_cancel_not_active', 'output_audio_buffer_clear_not_active'].includes(e.error && e.error.code)) CurioBridge.fail(s);
    },
    geminiMic: async function(s, on) {
      if (!on && !s.micWanted && !s.gettingMic) return;
      clearTimeout(s.micRelease);
      if (!on) {
        s.micRelease = setTimeout(function() {
          clearTimeout(s.micLimit);
          s.micWanted = false;
          if (s.stream) s.stream.getAudioTracks().forEach(function(t) { t.enabled = false; });
          if (s.micStarted && s.ws.readyState === WebSocket.OPEN) {
            s.ws.send(JSON.stringify({ type: 'mic', on: false })); CurioBridge.emit(s, 'thinking');
          } else {
            CurioBridge.emit(s, 'stopped');
            CurioBridge.emit(s, 'notice', { text: s.gettingMic ? 'Waiting for microphone permission. Allow access, then hold the button again.' : 'No microphone sound detected. Choose your microphone in Settings and check that it is not muted.' });
          }
          s.micStarted = false;
        }, 200); return;
      }
      s.micWanted = true; s.micStarted = false; CurioBridge.interrupt(s); s.text = ''; s.question = ''; s.turnDone = false;
      try {
        await s.ac.resume();
        if (!s.stream) {
          if (s.gettingMic) return;
          s.gettingMic = true;
          try { s.stream = await navigator.mediaDevices.getUserMedia({ audio: { deviceId: CurioBridge.device ? { exact: CurioBridge.device } : undefined, channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true } }); }
          finally { s.gettingMic = false; }
          if (s.closed) { s.stream.getTracks().forEach(function(t) { t.stop(); }); return; }
          var input = s.ac.createMediaStreamSource(s.stream);
          s.micAnalyser = s.ac.createAnalyser(); s.micAnalyser.fftSize = 256; input.connect(s.micAnalyser);
          s.processor = s.ac.createScriptProcessor(2048, 1, 1);
          var silence = s.ac.createGain(); silence.gain.value = 0;
          input.connect(s.processor); s.processor.connect(silence); silence.connect(s.ac.destination);
          s.processor.onaudioprocess = function(event) {
            if (!s.micWanted || s.closed || s.ws.readyState !== WebSocket.OPEN || s.ws.bufferedAmount > 65536) return;
            var samples = event.inputBuffer.getChannelData(0), ratio = s.ac.sampleRate / 16000;
            if (!s.micStarted) {
              var peak = 0; for (var n = 0; n < samples.length; n++) peak = Math.max(peak, Math.abs(samples[n]));
              if (peak < 0.002) return;
              s.ws.send(JSON.stringify({ type: 'mic', on: true })); s.micStarted = true;
            }
            var bytes = new Uint8Array(Math.floor(samples.length / ratio) * 2), view = new DataView(bytes.buffer);
            for (var i = 0; i < bytes.length / 2; i++) {
              var a = Math.floor(i * ratio), b = Math.min(samples.length, Math.floor((i + 1) * ratio)), value = 0;
              for (var j = a; j < b; j++) value += samples[j];
              value = Math.max(-1, Math.min(1, value / Math.max(1, b - a)));
              view.setInt16(i * 2, value < 0 ? value * 32768 : value * 32767, true);
            }
            var binary = ''; for (var k = 0; k < bytes.length; k++) binary += String.fromCharCode(bytes[k]);
            s.ws.send(JSON.stringify({ type: 'audio', data: btoa(binary) }));
          };
        }
        s.stream.getAudioTracks().forEach(function(t) { t.enabled = s.micWanted; });
        if (s.micWanted && !s.closed) { CurioBridge.emit(s, 'listening'); clearTimeout(s.micLimit); s.micLimit = setTimeout(function() { CurioBridge.geminiMic(s, false); }, 15000); }
        else CurioBridge.emit(s, 'notice', { text: 'Microphone ready. Hold it again to ask your question.' });
      } catch (error) { s.micWanted = false; CurioBridge.emit(s, 'stopped'); CurioBridge.emit(s, 'notice', { text: CurioBridge.micError(error) }); }
    },
    geminiConnect: function(s) {
      s.gemini = true; s.sources = []; s.nextAudio = 0; s.question = '';
      s.dc.onclose = null; s.pc.onconnectionstatechange = null; s.pc.close();
      s.analyser = s.ac.createAnalyser(); s.analyser.fftSize = 256;
      s.gain = s.ac.createGain(); s.analyser.connect(s.gain); s.gain.connect(s.ac.destination);
      s.ws = new WebSocket(s.url.replace(/^http/, 'ws') + '/live?ticket=' + encodeURIComponent(s.ticket));
      s.ws.onerror = function() { if (!s.closed) CurioBridge.fail(s); };
      s.ws.onclose = function() { if (!s.closed) CurioBridge.fail(s); };
      s.ws.onmessage = function(event) {
        if (s.closed) return;
        try {
          var e = JSON.parse(event.data);
          if (e.type === 'ready') { clearTimeout(s.timeout); CurioBridge.emit(s, 'ready'); }
          if (e.type === 'error') { CurioBridge.fail(s); return; }
          if (e.type === 'closed') { CurioBridge.stop(s, true); return; }
          if (e.type === 'thinking') CurioBridge.emit(s, 'thinking');
          if (e.type === 'interrupted') CurioBridge.interrupt(s);
          if (e.type === 'question-delta') s.question = (s.question + e.text).slice(0, 4000);
          if (e.type === 'answer-delta') { s.text = (s.text + e.text).slice(0, 4000); CurioBridge.emit(s, 'delta', { text: s.text }); }
          if (e.type === 'audio') {
            var raw = atob(e.data), bytes = new Uint8Array(raw.length); for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
            var view = new DataView(bytes.buffer), buffer = s.ac.createBuffer(1, bytes.length / 2, 24000), data = buffer.getChannelData(0);
            for (var j = 0; j < data.length; j++) data[j] = view.getInt16(j * 2, true) / 32768;
            var source = s.ac.createBufferSource(); source.buffer = buffer; source.connect(s.analyser);
            s.sources.push(source); s.playing = true; CurioBridge.emit(s, 'speaking');
            source.onended = function() {
              s.sources = s.sources.filter(function(v) { return v !== source; });
              if (!s.sources.length && s.turnDone && !s.closed) { s.playing = false; CurioBridge.emit(s, 'stopped'); }
            };
            var at = Math.max(s.ac.currentTime, s.nextAudio); source.start(at); s.nextAudio = at + buffer.duration;
          }
          if (e.type === 'turn-complete') {
            s.turnDone = true;
            if (s.question) CurioBridge.emit(s, 'question', { text: s.question });
            if (s.text) CurioBridge.emit(s, 'answer', { text: s.text });
            s.question = ''; s.text = '';
            if (!s.sources.length) { s.playing = false; CurioBridge.emit(s, 'stopped'); }
          }
        } catch (_) { CurioBridge.fail(s); }
      };
      var samples = new Float32Array(256);
      function rms(analyser) { if (!analyser) return 0; analyser.getFloatTimeDomainData(samples); var sum = 0; for (var i = 0; i < samples.length; i++) sum += samples[i] * samples[i]; return Math.min(1, Math.sqrt(sum / samples.length) * 5); }
      s.meter = setInterval(function() {
        if (!s.closed) CurioBridge.emit(s, 'level', { level: s.playing && s.gain.gain.value && s.ac.state === 'running' ? rms(s.analyser) : 0, mic: s.micWanted ? rms(s.micAnalyser) : 0 });
      }, 50);
    },
    connect: async function(target, url, context, seconds) {
      CurioBridge.stop(CurioBridge.current, false);
      var s = { target: target, url: url, abort: new AbortController(), seen: new Set(), text: '', playing: false, first: 0, started: 0 };
      CurioBridge.current = s;
      s.visibility = function() { if (document.hidden) CurioBridge.stop(s, true); };
      s.pagehide = function() { CurioBridge.stop(s, true); };
      document.addEventListener('visibilitychange', s.visibility); window.addEventListener('pagehide', s.pagehide);
      s.timeout = setTimeout(function() { CurioBridge.fail(s); }, 25000);
      s.limit = setTimeout(function() { CurioBridge.stop(s, true); }, Math.max(30, Math.min(300, seconds)) * 1000);
      try {
        s.ac = new (window.AudioContext || window.webkitAudioContext)();
        s.audio = document.createElement('audio'); s.audio.autoplay = true; s.audio.setAttribute('playsinline', '');
        s.resume = function() {
          if (s.closed) return;
          s.ac.resume().catch(function() {});
          if (s.audio.srcObject) s.audio.play().catch(function() { CurioBridge.emit(s, 'notice', { text: 'Tap again to enable audio. Subtitles remain available.' }); });
        };
        window.addEventListener('pointerdown', s.resume);
        s.pc = new RTCPeerConnection();
        s.sender = s.pc.addTransceiver('audio', { direction: 'sendrecv' }).sender;
        s.pc.onconnectionstatechange = function() {
          if (s.closed) return;
          if (s.pc.connectionState === 'failed' || s.pc.connectionState === 'disconnected') CurioBridge.fail(s);
        };
        s.pc.ontrack = function(e) {
          if (s.closed) return;
          var stream = e.streams[0] || new MediaStream([e.track]);
          s.audio.srcObject = stream;
          s.analyser = s.ac.createAnalyser(); s.analyser.fftSize = 256;
          s.ac.createMediaStreamSource(stream).connect(s.analyser);
          s.audio.play().catch(function() { CurioBridge.emit(s, 'notice', { text: 'Tap the panel to enable audio; you can read the subtitles meanwhile.' }); });
        };
        s.dc = s.pc.createDataChannel('oai-events');
        s.dc.onmessage = function(e) { try { CurioBridge.event(s, JSON.parse(e.data)); } catch (_) { CurioBridge.fail(s); } };
        s.dc.onclose = function() { if (!s.closed) CurioBridge.fail(s); };
        s.dc.onopen = function() { clearTimeout(s.timeout); CurioBridge.emit(s, 'ready'); };
        await s.pc.setLocalDescription(await s.pc.createOffer());
        var response = await fetch(url + '/session', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sdp: s.pc.localDescription.sdp, context: context }), signal: s.abort.signal });
        if (!response.ok) throw new Error('Session unavailable');
        var result = await response.json(); s.ticket = result.ticket;
        if (s.closed) { fetch(url + '/close', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ticket: s.ticket }) }).catch(function() {}); return; }
        if (result.provider === 'gemini') { CurioBridge.geminiConnect(s); return; }
        await s.pc.setRemoteDescription({ type: 'answer', sdp: result.sdp });
        var samples = new Float32Array(256);
        function rms(analyser) { if (!analyser) return 0; analyser.getFloatTimeDomainData(samples); var sum = 0; for (var i = 0; i < samples.length; i++) sum += samples[i] * samples[i]; return Math.min(1, Math.sqrt(sum / samples.length) * 5); }
        s.meter = setInterval(function() {
          if (s.closed) return;
          var level = s.playing && !s.audio.muted && !s.audio.paused && s.ac.state === 'running' ? rms(s.analyser) : 0;
          if (level > 0.01 && !s.first && s.started) s.first = performance.now() - s.started;
          CurioBridge.emit(s, 'level', { level: level, mic: s.micWanted ? rms(s.micAnalyser) : 0 });
        }, 50);
      } catch (_) { if (!s.closed) CurioBridge.fail(s); }
    }
  },
  CurioConnect__deps: ['$CurioBridge'],
  CurioMicrophones__deps: ['$CurioBridge'],
  CurioMicrophones: function(target) { CurioBridge.microphones(UTF8ToString(target)); },
  CurioSelectMicrophone__deps: ['$CurioBridge'],
  CurioSelectMicrophone: function(device) { CurioBridge.device = UTF8ToString(device); },
  CurioConnect: function(target, url, context, seconds) { CurioBridge.connect(UTF8ToString(target), UTF8ToString(url), JSON.parse(UTF8ToString(context)), seconds); },
  CurioCommand__deps: ['$CurioBridge'],
  CurioCommand: function(type, value) {
    var s = CurioBridge.current; if (!s || s.closed) return;
    type = UTF8ToString(type); value = UTF8ToString(value);
    if (type === 'close') CurioBridge.stop(s, false);
    else if (type === 'ask') CurioBridge.ask(s, value);
    else if (type === 'mic') CurioBridge.mic(s, value === 'on');
    else if (type === 'mute') { s.audio.muted = value === 'on'; if (s.gain) s.gain.gain.value = value === 'on' ? 0 : 1; }
  }
});
