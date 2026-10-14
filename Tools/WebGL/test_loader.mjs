// Run with: node Tools/WebGL/test_loader.mjs (no packages required).
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { runInNewContext } from 'node:vm';

const root = new URL('../../Assets/WebGLTemplates/Photosynthesis/', import.meta.url);
const html = readFileSync(new URL('index.html', root), 'utf8');
const png = readFileSync(new URL('loading-background.png', root));
assert.equal(createHash('sha256').update(png).digest('hex'),
  'b7f1b4e8a0ba1f8727519fc8254030bc0775cd9f26b9647401a4fdc05f4611a3', 'Keep the supplied PNG unchanged');
const script = html.match(/<script>\s*([\s\S]*?)<\/script>/)[1]
  .replace(/var config = \{[\s\S]*?\n        \};/, 'var config = {};');

function element() {
  return {
    style: {}, attributes: {}, children: [], textContent: '', hidden: true,
    classList: new Set(),
    setAttribute(k, v) { this.attributes[k] = v; },
    appendChild(child) { this.children.push(child); },
    remove() { this.removed = true; },
    focus() { this.focused = true; }
  };
}

async function start({ preview = false, split = false, networkFailure = false } = {}) {
  const els = Object.fromEntries(['.loader', '.fill', '.bar', '.tip', '.pct', '.note', '.motes', '.play', '.warn', 'canvas']
    .map(key => [key, element()]));
  els['.loader'].querySelector = key => els[key];
  const timers = [], intervals = new Set();
  let onProgress, resolve, reject, calls = 0;
  const runtime = new Promise((yes, no) => { resolve = yes; reject = no; });
  const context = {
    document: { getElementById: () => els.canvas, querySelector: key => els[key], createElement: element },
    location: { search: preview ? '?loader' : '' },
    setTimeout: (fn, ms) => timers.push({ fn, ms }),
    setInterval: fn => { intervals.add(fn); return fn; },
    clearInterval: fn => intervals.delete(fn),
    createUnityInstance: (canvas, config, callback) => { calls++; onProgress = callback; return runtime; },
    Blob, URL,
    fetch: async () => {
      const chunks = [new Uint8Array(2), new Uint8Array(3)];
      return { ok: !networkFailure, status: 503, body: { getReader: () => ({
        read: async () => chunks.length ? { value: chunks.shift() } : { done: true }
      }) } };
    }
  };
  runInNewContext(split ? script.replace('var dataParts = null, dataBytes = 0;',
    'var dataParts = ["part1", "part2"], dataBytes = 10;') : script, context);
  const flush = () => new Promise(yes => setImmediate(yes));
  await flush();
  return { els, timers, intervals, resolve, reject, flush, progress: p => onProgress(p), calls: () => calls };
}

const normal = await start();
assert.equal(normal.calls(), 1);
assert.equal(normal.els['.motes'].children.length, 16);
normal.progress(0);
assert.equal(normal.els['.fill'].style.width, '0%', 'No fake initial fill');
normal.progress(.5);
normal.progress(.2);
normal.progress(NaN);
assert.equal(normal.els['.fill'].style.width, '50%', 'Progress never retreats or accepts NaN');
normal.progress(1);
assert.equal(normal.els['.bar'].attributes['aria-valuenow'], 99, 'Wait for runtime readiness');
assert.equal(normal.timers.length, 0, 'No fade while startup is pending');
normal.resolve();
await normal.flush();
assert.equal(normal.els['.fill'].style.width, '100%');
assert.equal(normal.timers[0].ms, 700, 'Fill settles and holds before fade');
normal.timers.shift().fn();
assert(normal.els['.loader'].classList.has('out'));
assert(!normal.els['.loader'].removed);
assert.equal(normal.timers[0].ms, 800);
normal.timers.shift().fn();
assert(normal.els['.loader'].removed && normal.els.canvas.focused);

const failed = await start();
failed.reject(new Error('startup failed'));
await failed.flush();
assert(failed.els['.loader'].classList.has('err'));
assert.equal(failed.els['.tip'].attributes.role, 'alert');
assert.match(failed.els['.tip'].textContent, /startup failed/);
assert.equal(failed.timers.length, 0);

const split = await start({ split: true });
assert.equal(split.calls(), 1);
assert.equal(split.els['.fill'].style.width, '90%', 'Download bytes retain their 90% share');
split.progress(.5);
assert.equal(split.els['.bar'].attributes['aria-valuenow'], 95);
split.resolve();
await split.flush();
assert.equal(split.els['.fill'].style.width, '100%');
const offline = await start({ split: true, networkFailure: true });
assert.equal(offline.calls(), 0);
assert.match(offline.els['.tip'].textContent, /HTTP 503/);

const preview = await start({ preview: true });
assert.equal(preview.calls(), 0);
assert.equal(preview.els['.play'].hidden, false);
assert.match(preview.els['.note'].textContent, /Preview/);
for (let i = 0; i < 170; i++) for (const tick of preview.intervals) tick();
assert.equal(preview.els['.fill'].style.width, '100%');
assert.equal(preview.intervals.size, 0);
assert.equal(preview.timers.length, 0, 'Design preview remains visible');
console.log('PASS: exact artwork, real progress, completion/fade, startup error, split download/error, preview cleanup');
