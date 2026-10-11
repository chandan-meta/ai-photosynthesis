import { readFileSync, writeFileSync, renameSync, existsSync } from 'node:fs';

export function integer(value: string | undefined, fallback: number, min: number, max: number): number {
  const n = Number(value ?? fallback);
  if (!Number.isInteger(n) || n < min || n > max) throw new Error('Invalid numeric configuration');
  return n;
}
export function context(input: unknown) {
  const c = input as Record<string, unknown>;
  if (!c || typeof c !== 'object' || Array.isArray(c)) throw new Error('Invalid context');
  const text = (key: string, max = 240) => typeof c[key] === 'string' ? c[key].slice(0, max) : '';
  return {
    scene: text('scene', 80), beat: text('beat', 40), topic: text('topic'), task: text('task'),
    language: c.language === 'hi' ? 'hi' : 'en', hintOnly: c.hintOnly === true,
    clues: Array.isArray(c.clues) ? c.clues.filter((v): v is string => typeof v === 'string').slice(0, 3).map(v => v.slice(0, 40)) : []
  };
}
export function turns(input: unknown): { role: 'user' | 'assistant'; content: string }[] {
  if (!Array.isArray(input)) return [];
  return input.slice(-12).filter(v => v && (v.role === 'user' || v.role === 'assistant') && typeof v.text === 'string')
    .map(v => ({ role: v.role, content: v.text.slice(0, 2000) }));
}
export function originAllowed(origin: string | undefined, allowed: Set<string>, native: boolean) {
  return origin === undefined ? native : allowed.has(origin);
}
export function rateLimited(rates: Map<string, { at: number; count: number }>, ip: string, now: number) {
  for (const [key, rate] of rates) if (now - rate.at > 60000) rates.delete(key);
  const existing = rates.get(ip);
  if (!existing && rates.size >= 10000) return true;
  const rate = existing ?? { at: now, count: 0 };
  rate.count++; rates.set(ip, rate);
  return rate.count > 10;
}

// Single-instance service: persist reservations before upstream spending, including failed attempts.
export class Budget {
  file: string; maximum: number;
  constructor(file: string, maximum: number) { this.file = file; this.maximum = maximum; }
  reserve(now = new Date()) {
    const day = now.toISOString().slice(0, 10);
    let usage = { day, count: 0 };
    if (existsSync(this.file)) {
      const saved = JSON.parse(readFileSync(this.file, 'utf8'));
      if (!Number.isInteger(saved.count) || saved.count < 0 || typeof saved.day !== 'string') throw new Error('Invalid budget ledger');
      if (saved.day === day) usage = saved;
    }
    if (usage.count >= this.maximum) return false;
    usage.count++;
    writeFileSync(this.file + '.tmp', JSON.stringify(usage), { mode: 0o600 }); renameSync(this.file + '.tmp', this.file);
    return true;
  }
}
