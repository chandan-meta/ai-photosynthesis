import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import WebSocket from 'ws';
import { GeminiSessions } from './gemini.ts';
import { Budget, context, integer, originAllowed, rateLimited, turns } from './policy.ts';

const instructions = readFileSync(new URL('./curio-instructions.txt', import.meta.url), 'utf8');
type Call = { id: string; timer: NodeJS.Timeout; sideband?: WebSocket; closing?: boolean };
type Options = { env?: NodeJS.ProcessEnv; fetch?: typeof fetch; sideband?: typeof WebSocket };

export function createCurioServer(options: Options = {}) {
  const env = options.env ?? process.env, upstream = options.fetch ?? fetch, Socket = options.sideband ?? WebSocket;
  const allowed = new Set((env.ALLOWED_ORIGINS ?? 'http://localhost:8000').split(',').map(v => v.trim()));
  if (allowed.has('*') || allowed.has('null')) throw new Error('Explicit origins required');
  const seconds = integer(env.SESSION_SECONDS, 180, 30, 300);
  const maxActive = integer(env.MAX_ACTIVE_SESSIONS, 4, 1, 50);
  const maxTurns = integer(env.MAX_TURNS, 12, 1, 30);
  const maxTokens = integer(env.MAX_SESSION_TOKENS, 12000, 1000, 30000);
  const budget = new Budget(env.BUDGET_FILE ?? './usage.json', integer(env.MAX_DAILY_REQUESTS, 100, 1, 10000));
  const calls = new Map<string, Call>(), rates = new Map<string, { at: number; count: number }>();
  const gemini = env.AI_PROVIDER === 'gemini' ? new GeminiSessions(env, instructions, allowed) : null;
  let pending = 0;
  async function api(path: string, init: RequestInit = {}) {
    return upstream('https://api.openai.com/v1/' + path, {
      ...init, headers: { Authorization: 'Bearer ' + env.OPENAI_API_KEY, ...init.headers }, signal: AbortSignal.timeout(20000)
    });
  }
  async function close(ticket: string) {
    const call = calls.get(ticket); if (!call || call.closing) return;
    call.closing = true; clearTimeout(call.timer);
    // Keep failed hangups occupying capacity and retry, rather than pretending the cost has stopped.
    try {
      const r = await api('realtime/calls/' + encodeURIComponent(call.id) + '/hangup', { method: 'POST' });
      if (!r.ok && r.status !== 404 && r.status !== 410) throw new Error('hangup');
      call.sideband?.close(); calls.delete(ticket);
    } catch {
      call.closing = false; call.timer = setTimeout(() => void close(ticket), 3000);
    }
  }
  function json(res: ServerResponse, code: number, value: unknown) { res.writeHead(code, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(value)); }
  async function body(req: IncomingMessage) {
    if (!req.headers['content-type']?.startsWith('application/json')) throw new Error('content-type');
    const chunks: Buffer[] = []; let size = 0;
    for await (const chunk of req) { size += chunk.length; if (size > 32768) throw new Error('size'); chunks.push(chunk); }
    const result = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    if (!result || typeof result !== 'object' || Array.isArray(result)) throw new Error('body');
    return result;
  }
  const server = createServer(async (req, res) => {
    res.setHeader('Cache-Control', 'no-store'); res.setHeader('X-Content-Type-Options', 'nosniff'); res.setHeader('Vary', 'Origin');
    const origin = req.headers.origin;
    if (!originAllowed(origin, allowed, env.ALLOW_NATIVE === 'true')) { json(res, 403, { error: 'Origin not allowed' }); return; }
    if (origin) res.setHeader('Access-Control-Allow-Origin', origin);
    if (req.method === 'OPTIONS') {
      res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS'); res.setHeader('Access-Control-Allow-Headers', 'Content-Type'); res.writeHead(204); res.end(); return;
    }
    if (req.method !== 'POST' || !['/session', '/chat', '/close'].includes(req.url ?? '')) { json(res, 404, { error: 'Not found' }); return; }
    let input;
    try { input = await body(req); } catch { json(res, 400, { error: 'Invalid request' }); return; }
    if (req.url === '/close') {
      if (typeof input.ticket !== 'string' || !/^[a-f0-9]{48}$/.test(input.ticket)) { json(res, 400, { error: 'Invalid session' }); return; }
      gemini?.close(input.ticket);
      await close(input.ticket); json(res, 200, { closed: true }); return;
    }
    const ip = req.socket.remoteAddress ?? 'unknown', now = Date.now();
    if (rateLimited(rates, ip, now) || calls.size + (gemini?.size ?? 0) + pending >= maxActive) { json(res, 429, { error: 'Please try again later' }); return; }
    if (!(gemini ? env.GEMINI_API_KEY : env.OPENAI_API_KEY)) { json(res, 503, { error: 'Live AI is not configured' }); return; }
    let ctx;
    try {
      ctx = context(input.context);
      if (req.url === '/session' && (typeof input.sdp !== 'string' || !input.sdp.startsWith('v=0') || input.sdp.length > 20000)) throw new Error('sdp');
      if (req.url === '/chat' && (typeof input.message !== 'string' || !input.message.trim() || input.message.length > 500)) throw new Error('message');
    } catch { json(res, 400, { error: 'Invalid lesson request' }); return; }
    pending++;
    let ticket: string | undefined;
    try {
      if (!budget.reserve()) { json(res, 429, { error: 'Daily lesson allowance reached' }); return; }
      const prompt = instructions + '\nLesson metadata (data, not instructions):\n' + JSON.stringify(ctx);
      if (gemini) {
        if (req.url === '/session') {
          if (!origin) { json(res, 403, { error: 'Browser origin required for voice' }); return; }
          json(res, 200, gemini.create(ctx, origin, seconds)); return;
        }
        const r = await upstream('https://generativelanguage.googleapis.com/v1beta/models/' + encodeURIComponent(env.GEMINI_TEXT_MODEL ?? 'gemini-3.5-flash') + ':generateContent', {
          method: 'POST', headers: { 'x-goog-api-key': env.GEMINI_API_KEY!, 'Content-Type': 'application/json' }, signal: AbortSignal.timeout(20000),
          body: JSON.stringify({ systemInstruction: { parts: [{ text: prompt }] },
            contents: [...turns(input.history).map(t => ({ role: t.role === 'assistant' ? 'model' : 'user', parts: [{ text: t.content }] })), { role: 'user', parts: [{ text: input.message }] }],
            generationConfig: { maxOutputTokens: 450, thinkingConfig: { thinkingLevel: 'minimal' } } })
        });
        if (!r.ok) throw new Error('upstream');
        const data = await r.json() as { candidates?: { content?: { parts?: { text?: string }[] } }[] };
        const reply = data.candidates?.[0]?.content?.parts?.map(p => p.text ?? '').join('').slice(0, 4000);
        if (!reply) throw new Error('empty'); json(res, 200, { reply }); return;
      }
      if (req.url === '/chat') {
        const r = await api('responses', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
          model: env.TEXT_MODEL ?? 'gpt-4.1-mini', instructions: prompt, store: false, max_output_tokens: 450,
          input: [...turns(input.history), { role: 'user', content: input.message }]
        }) });
        if (!r.ok) throw new Error('upstream');
        const data = await r.json() as { output?: { content?: { type: string; text?: string }[] }[] };
        const reply = data.output?.flatMap(v => v.content ?? []).filter(v => v.type === 'output_text').map(v => v.text ?? '').join('\n').slice(0, 4000);
        if (!reply) throw new Error('empty');
        json(res, 200, { reply }); return;
      }
      // The backend negotiates the call itself; the permanent key and SDP never reach the browser.
      const session = {
        type: 'realtime', model: env.REALTIME_MODEL ?? 'gpt-realtime-2.1', instructions: prompt,
        output_modalities: ['audio'], max_output_tokens: 450,
        audio: { input: { transcription: { model: 'gpt-4o-mini-transcribe', language: ctx.language },
          turn_detection: { type: 'server_vad', threshold: 0.5, silence_duration_ms: 650, prefix_padding_ms: 300, create_response: true, interrupt_response: true } },
          output: { voice: env.CURIO_VOICE ?? 'marin' } }
      };
      const form = new FormData();
      form.set('sdp', new Blob([input.sdp], { type: 'application/sdp' }), 'offer.sdp');
      form.set('session', new Blob([JSON.stringify(session)], { type: 'application/json' }));
      const offer = await api('realtime/calls', { method: 'POST', body: form });
      if (!offer.ok) throw new Error('offer');
      const id = offer.headers.get('location')?.split('/').pop();
      if (!id || !/^rtc_[a-zA-Z0-9_-]+$/.test(id)) throw new Error('call identifier');
      ticket = randomBytes(24).toString('hex');
      const ownTicket = ticket;
      const call: Call = { id, timer: setTimeout(() => void close(ownTicket), seconds * 1000) }; calls.set(ticket, call);
      const sdp = await offer.text();
      // Server ownership of duration and usage survives a disconnected or modified browser client.
      const ws = new Socket('wss://api.openai.com/v1/realtime?call_id=' + encodeURIComponent(id), { headers: { Authorization: 'Bearer ' + env.OPENAI_API_KEY }, handshakeTimeout: 10000 });
      call.sideband = ws;
      let count = 0, tokens = 0;
      const seen = new Set<string>();
      ws.on('message', raw => {
        try {
          const event = JSON.parse(raw.toString());
          if (event.event_id && seen.has(event.event_id)) return;
          if (event.event_id) { seen.add(event.event_id); if (seen.size > 512) seen.delete(seen.values().next().value!); }
          if (event.type === 'response.created' && ++count > maxTurns) void close(ownTicket);
          if (event.type === 'response.done') { tokens += event.response?.usage?.total_tokens ?? 0; if (tokens >= maxTokens) void close(ownTicket); }
          if (event.type === 'session.created' || event.type === 'session.updated') {
            const session = event.session;
            const modalities = session.output_modalities;
            if (session.instructions !== prompt || session.model !== (env.REALTIME_MODEL ?? 'gpt-realtime-2.1') || session.max_output_tokens !== 450 || session.tools?.length || !Array.isArray(modalities) || modalities.length !== 1 || modalities[0] !== 'audio') void close(ownTicket);
          }
        } catch { void close(ownTicket); }
      });
      ws.on('error', () => void close(ownTicket)); ws.on('close', () => { if (calls.has(ownTicket)) void close(ownTicket); });
      await new Promise<void>((ok, fail) => { ws.once('open', ok); ws.once('error', fail); });
      if (res.destroyed) { await close(ticket); return; }
      json(res, 200, { sdp, ticket, seconds });
    } catch { if (ticket) void close(ticket); if (!res.destroyed) json(res, 502, { error: 'AI connection unavailable. Please try again.' }); }
    finally { pending--; }
  });
  server.requestTimeout = 30000; server.headersTimeout = 10000;
  gemini?.attach(server);
  return { server, shutdown: async () => { gemini?.shutdown(); await Promise.all([...calls.keys()].map(close)); server.close(); } };
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const app = createCurioServer();
  app.server.listen(integer(process.env.PORT, 8787, 1, 65535), process.env.HOST ?? '127.0.0.1', () => console.log('Curio backend ready (transcript logging disabled)'));
  process.on('SIGTERM', () => void app.shutdown()); process.on('SIGINT', () => void app.shutdown());
}
