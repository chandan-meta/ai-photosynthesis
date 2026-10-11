import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

function bridge() {
  const events = [], sent = [], library = {}, listeners = new Map();
  const scope = { LibraryManager: { library }, mergeInto: Object.assign, SendMessage: (_, __, json) => events.push(JSON.parse(json)),
    performance: { now: () => 1000 }, clearInterval, clearTimeout, setTimeout, AbortController, WebSocket: { OPEN: 1 }, btoa,
    document: { removeEventListener: key => listeners.delete(key) }, window: { removeEventListener: key => listeners.delete(key) },
    fetch: async () => new Response('{}'), console };
  vm.runInNewContext(readFileSync(new URL('../../../Assets/Plugins/WebGL/CurioRealtime.jslib', import.meta.url), 'utf8'), scope);
  scope.CurioBridge = library.$CurioBridge;
  const b = scope.CurioBridge;
  const s = { target: 'Module', seen: new Set(), text: '', dc: { readyState: 'open', send: raw => sent.push(JSON.parse(raw)) }, abort: new AbortController() };
  b.current = s; return { b, s, events, sent, scope };
}
test('interruption discards stale transcript and queues the newest typed turn until cancellation finishes', () => {
  const { b, s, events, sent } = bridge();
  b.event(s, { type: 'response.created', response: { id: 'old' } });
  b.ask(s, 'Why?'); b.ask(s, 'Explain it simply.');
  assert.equal(sent.filter(e => e.type === 'response.create').length, 0);
  b.event(s, { type: 'response.output_audio_transcript.delta', response_id: 'old', delta: 'stale' });
  assert.equal(events.filter(e => e.type === 'delta').length, 0);
  b.event(s, { type: 'response.done', response: { id: 'old', status: 'cancelled' } });
  assert.equal(sent.filter(e => e.type === 'response.create').length, 1);
  assert.equal(sent.find(e => e.type === 'conversation.item.create').item.content[0].text, 'Explain it simply.');
});
test('Gemini microphone resamples browser audio to PCM16 and preserves the release tail', async () => {
  const { b, s, sent, scope, events } = bridge(); let enabled = true;
  const track = { get enabled() { return enabled; }, set enabled(value) { enabled = value; } };
  b.device = 'real-mic';
  scope.navigator = { mediaDevices: { getUserMedia: async options => { assert.equal(options.audio.deviceId.exact, 'real-mic'); return { getAudioTracks: () => [track] }; } } };
  s.gemini = true; s.sources = []; s.ws = { readyState: 1, bufferedAmount: 0, send: raw => sent.push(JSON.parse(raw)) };
  s.ac = { sampleRate: 48000, resume: async () => {}, createMediaStreamSource: () => ({ connect() {} }), createAnalyser: () => ({}),
    createScriptProcessor: () => ({ connect() {} }), createGain: () => ({ gain: {}, connect() {} }), destination: {} };
  await b.mic(s, true);
  s.processor.onaudioprocess({ inputBuffer: { getChannelData: () => new Float32Array(2048) } });
  assert.equal(sent.length, 0);
  const frame = { inputBuffer: { getChannelData: () => new Float32Array(2048).fill(.25) } };
  s.processor.onaudioprocess(frame);
  const pcm = Buffer.from(sent.find(e => e.type === 'audio').data, 'base64');
  assert.equal(pcm.length, Math.floor(2048 / 3) * 2); assert.equal(pcm.readInt16LE(0), 8191);
  await b.mic(s, false);
  assert.equal(s.micWanted, true);
  await new Promise(ok => setTimeout(ok, 230));
  assert.equal(s.micWanted, false); assert.equal(enabled, false);
  assert.equal(sent.at(-1).type, 'mic'); assert.equal(sent.at(-1).on, false);
  assert.equal(events.at(-1).type, 'thinking');
  const eventCount = events.length; await b.mic(s, false); assert.equal(events.length, eventCount);
  const count = sent.length; s.processor.onaudioprocess(frame); assert.equal(sent.length, count);
});

test('silent input never sends a Gemini turn and release clears listening', async () => {
  const { b, s, sent, events } = bridge();
  s.gemini = true; s.sources = []; s.stream = { getAudioTracks: () => [{ enabled: true }] };
  s.ws = { readyState: 1, send: raw => sent.push(JSON.parse(raw)) }; s.ac = { resume: async () => {} };
  await b.mic(s, true); await b.mic(s, false);
  await new Promise(ok => setTimeout(ok, 230));
  assert.equal(sent.length, 0); assert.equal(s.micWanted, false);
  assert.equal(events.at(-2).type, 'stopped'); assert.match(events.at(-1).text, /No microphone sound/);
});

test('browser device listing releases the permission stream and explains blocked access', async () => {
  const { b, scope, events } = bridge(); let stopped = false;
  scope.navigator = { mediaDevices: { getUserMedia: async () => ({ getTracks: () => [{ stop: () => stopped = true }] }),
    enumerateDevices: async () => [{ kind: 'audioinput', deviceId: 'usb', label: 'USB Mic' }, { kind: 'audiooutput', deviceId: 'speaker' }] } };
  await b.microphones('HUD');
  assert.equal(stopped, true); assert.equal(events.at(-1).devices.length, 1); assert.equal(events.at(-1).devices[0].label, 'USB Mic');
  scope.navigator.mediaDevices.getUserMedia = async () => { throw { name: 'NotAllowedError' }; };
  await b.microphones('HUD'); assert.match(events.at(-1).error, /access blocked/);
});
test('audio completion, duplicate events and cleanup do not leave a talking character or microphone', () => {
  const { b, s, events } = bridge(); let stopped = 0, paused = 0, closed = 0;
  s.stream = { getTracks: () => [{ stop: () => stopped++ }] };
  s.audio = { pause: () => paused++ }; s.pc = { close: () => closed++ };
  b.event(s, { type: 'output_audio_buffer.started', event_id: 'start' });
  b.event(s, { type: 'output_audio_buffer.started', event_id: 'start' });
  assert.equal(events.filter(e => e.type === 'speaking').length, 1);
  b.event(s, { type: 'output_audio_buffer.cleared' }); assert.equal(s.playing, false);
  b.stop(s, true); b.stop(s, true);
  assert.equal(stopped, 1); assert.equal(paused, 1); assert.equal(closed, 1); assert.equal(b.current, null);
  const count = events.length; b.emit(s, 'answer', { text: 'late' }); assert.equal(events.length, count);
});
