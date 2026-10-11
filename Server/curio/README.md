# Mr. Curio voice backend

For Gemini, set `AI_PROVIDER=gemini` and `GEMINI_API_KEY` in the private `.env`. Browser voice uses the Gemini Live WebSocket relay with 16 kHz PCM input and 24 kHz PCM output; typed fallback uses Gemini 3.5 Flash. The current local configuration has live mode enabled. The key is never compiled into Unity or returned to the browser. Run `node Tools/ServeWebGL.mjs` from the project directory after building WebGL, then open `http://localhost:8000`. The API stays on `http://localhost:8787`. All existing origin, duration, turn, request and budget limits still apply. The relay accepts only questions, mic state and audio frames; clients cannot change the system prompt or invoke tools.

`npm test` uses fake credentials and makes no paid requests. `node test-live.mjs` is an explicit account-backed voice check using the generated WAV fixture from `Tools/Curio/TestSpeech.ps1`.

For browser microphone setup, open Settings → Audio → Allow / refresh microphones, grant microphone access for the game, then choose the physical input from the Microphone dropdown. Device names are available after permission is granted. Hold the chat microphone while speaking and release to send. Gemini capture stops automatically after 15 seconds; silent input shows a microphone notice instead of asking the model to interpret silence. A virtual headset input may be present even when no headset is attached, so choose the real headset or USB microphone explicitly.

The browser calls `POST /session` with its WebRTC offer and small lesson context. This server applies the photosynthesis instructions and negotiates the call through OpenAI's documented server-side WebRTC endpoint using its private API key. It returns only the SDP answer and a random local ticket, then attaches a private sideband connection. Sideband events enforce a session timer, turn count, output limit and allowed session configuration. `POST /close` hangs up upstream sessions. Typed text uses `POST /chat` and Responses with `store: false`.

## Run locally

Install Node.js 24 or newer, copy `.env.example` to `.env`, add the server-side `OPENAI_API_KEY`, and set `ALLOWED_ORIGINS` to the exact origin serving the WebGL build. Then run `npm ci`, `npm test`, `npm run check` and `npm start`.

The default listener is `127.0.0.1:8787`, intended for local development. Set `HOST=0.0.0.0` only behind a TLS reverse proxy that limits access, requests, body size and timeouts. Serve both the game and API through HTTPS in production. Configure `Resources/curio_ai_settings.json` with that API's HTTPS URL and `"mock": false`. The current local build enables live mode for testing; set `"mock": true` to return to offline practice. No credentials are included in Unity.

Windows builds retain push-to-talk through the existing Windows speech recognizer and local Curio answers. Configured text fallback uses `/chat` when the service is intentionally configured to accept origin-less desktop requests, preferably behind a private authenticated gateway. Direct Realtime WebRTC voice is supported by browser WebGL. Mic denial, browser audio autoplay rules, and temporary connection failures keep the typed input available.

## Limits and operating notes

This is a single-instance service: the in-memory origin-rate table and active sessions are process-local, and the daily budget ledger is a single-writer file. Keep one server process and a persistent, private volume for `BUDGET_FILE`; use a shared transactional store and coordinated session registry before adding replicas. The default `ALLOW_NATIVE=false` rejects requests without an HTTP Origin. Enabling it trusts any origin-less client that can reach the service, so only set it behind an authenticated private gateway. Avoid logging request bodies, transcripts, ephemeral secrets or IP addresses. Back up or rotate the minimal daily usage counter as required by the school deployment.

The model and voice need to be enabled for the configured OpenAI account. Model availability, billing and live latency are verified only after connecting with real credentials. The browser receives no OpenAI credential; the backend keeps its API key and owns Realtime SDP negotiation and the session.
