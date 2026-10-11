// Explicit, account-backed smoke test. npm test never spends API credits.
import WebSocket from 'ws';
import { readFileSync } from 'node:fs';
import { setTimeout as sleep } from 'node:timers/promises';
const origin = 'http://localhost:8000', base = 'http://localhost:8787';
const session = await fetch(base + '/session', { method: 'POST', headers: { Origin: origin, 'Content-Type': 'application/json' },
  body: JSON.stringify({ sdp: 'v=0\r\n', context: { language: 'en', beat: 'FreeExplore', topic: 'water', clues: ['water', 'co2', 'sunlight'] } }) });
if (!session.ok) throw new Error('Session HTTP ' + session.status);
const { ticket } = await session.json();
const socket = new WebSocket(base.replace('http:', 'ws:') + '/live?ticket=' + ticket, { headers: { Origin: origin } });
let question = '', answer = '', audioBytes = 0;
const done = new Promise((ok, fail) => {
  const timer = setTimeout(() => fail(new Error('Live answer timed out')), 30000);
  socket.on('error', () => { clearTimeout(timer); fail(new Error('Live socket failed')); });
  socket.on('message', async raw => {
    const e = JSON.parse(raw.toString());
    if (e.type === 'ready') {
      const wave = readFileSync(new URL('../../Logs/CurioAI/speech/topic.wav', import.meta.url));
      let at = 12, audio;
      while (at + 8 <= wave.length) { const size = wave.readUInt32LE(at + 4); if (wave.toString('ascii', at, at + 4) === 'data') { audio = wave.subarray(at + 8, at + 8 + size); break; } at += 8 + size + size % 2; }
      if (!audio) { clearTimeout(timer); fail(new Error('Missing test PCM')); return; }
      socket.send(JSON.stringify({ type: 'mic', on: true }));
      for (let i = 0; i < audio.length; i += 2048) { socket.send(JSON.stringify({ type: 'audio', data: audio.subarray(i, i + 2048).toString('base64') })); await sleep(25); }
      socket.send(JSON.stringify({ type: 'mic', on: false }));
    }
    if (e.type === 'question-delta') question += e.text;
    if (e.type === 'answer-delta') answer += e.text;
    if (e.type === 'audio') audioBytes += Buffer.from(e.data, 'base64').length;
    if (e.type === 'error') { clearTimeout(timer); fail(new Error('Gemini rejected live setup')); }
    if (e.type === 'turn-complete') { clearTimeout(timer); ok(); }
  });
});
try {
  await done;
  if (!/water/i.test(question) || !answer || !audioBytes) throw new Error('Missing transcription, answer or voice');
  console.log(JSON.stringify({ question, answer, audioBytes, passed: true }));
} finally { socket.close(); await fetch(base + '/close', { method: 'POST', headers: { Origin: origin, 'Content-Type': 'application/json' }, body: JSON.stringify({ ticket }) }); }
