#!/usr/bin/env node
/**
 * Steps 2 to 4 of the quotes and life maxims import (step 1 is public-domain-candidates.mjs). Development tool, run by hand.
 *
 * Step 2, choice and translation (outside this script, by a language model): the candidates it is given are kept as <work>/rated.jsonl; the
 *   answers are <work>/batches/out-NN.jsonl: {"n", "useful": 0-3, "why", "vi" (for 2 and 3)}.
 * Step 3: node tools/public-domain-build.mjs select --work <work>
 *   keeps the accepted sentences that step 1 still accepts (<work>/standalone.jsonl), validates and removes duplicates, writes
 *   <work>/selected.jsonl and the check files <work>/check/back-NN.jsonl (Vietnamese only) and ref-NN.jsonl. A second, independent model run
 *   translates back before it sees the English, compares, and says whether it knows the sentence as somebody else's words:
 *   verdict-NN.jsonl {"n", "verdict": "ok"|"doubtful"|"wrong", "back", "note", "foreign": true when it is another author's}.
 * Step 4: node tools/public-domain-build.mjs write --work <work>
 *   writes Assets/Quotes/data/public-domain-*.jsonl, the report and the review sample.
 */
import { readFileSync, writeFileSync, readdirSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { WORKS } from './public-domain-works.mjs';
import { blockedVietnamese } from './expression-content-filter.mjs';

const [command, ...args] = process.argv.slice(2);
const option = (name, fallback) => { const i = args.indexOf(`--${name}`); return i >= 0 && args[i + 1] ? args[i + 1] : fallback; };
const work = option('work');
if (!['select', 'write'].includes(command) || !work) { console.error('Usage: node tools/public-domain-build.mjs select|write --work <folder> [--date yyyy-mm-dd] [--model <name>]'); process.exit(2); }
const date = option('date', '2026-11-26');
const model = option('model', 'Claude Sonnet (Anthropic), machine draft');

const TARGETS = { quote: 600, maxim: 600 };
const NAMES = { quote: 'quotes', maxim: 'maxims' };
const RESERVE = 1.1;

const lines = (path) => readFileSync(path, 'utf8').split(/\r?\n/).filter((l) => l.trim()).map((l) => JSON.parse(l));
const jsonl = (rows) => rows.map((r) => JSON.stringify(r)).join('\n') + '\n';
const key = (text) => text.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
const viKey = (text) => text.normalize('NFC').toLowerCase().replace(/[\s.,!?]+/g, ' ').trim();
const diacritics = /[ăâđêôơưàáảãạằắẳẵặầấẩẫậèéẻẽẹềếểễệìíỉĩịòóỏõọồốổỗộờớởỡợùúủũụừứửữựỳýỷỹỵ]/i;
const answers = (folder, pattern) => new Map(readdirSync(folder).filter((f) => pattern.test(f)).sort().flatMap((f) => lines(join(folder, f))).map((o) => [o.n, o]));

function fault(text) {
  if (text.length < 8 || text.length > 180) return 'length';
  if (/[()\[\]{}<>"“”\/\\|_*#@=~^]/.test(text)) return 'markup or quotation marks';
  if (!/^[\p{Script=Latin}\p{M}\d\s,'.!?:;%-]+$/u.test(text)) return 'unexpected characters';
  if (!diacritics.test(text)) return 'no Vietnamese diacritics';
  if (blockedVietnamese(text)) return 'blocked word';
  return '';
}

/** Two sentences that share nearly all their words are one saying told twice. */
const tokens = (text) => new Set(key(text).split(' '));
const alike = (a, b) => { let shared = 0; for (const t of a) if (b.has(t)) shared++; return shared / (a.size + b.size - shared) >= 0.75; };

function select() {
  const rated = lines(join(work, 'rated.jsonl'));
  const standalone = new Set(lines(join(work, 'standalone.jsonl')).map((c) => `${c.file}|${c.text}`));
  const answer = answers(join(work, 'batches'), /^out-.*\.jsonl$/);
  const existing = JSON.parse(readFileSync('Assets/Quotes/quotes.json', 'utf8')).groups.flatMap((g) => g.quotes.map((q) => ({ english: q.english, vietnamese: q.vietnamese })));
  for (const file of readdirSync('Assets/Quotes/data').filter((f) => /^wiktionary-.*\.jsonl$/.test(f))) existing.push(...lines(join('Assets/Quotes/data', file)).map((r) => ({ english: r.text, vietnamese: r.translations[0].text })));
  const english = new Set(existing.map((q) => key(q.english))), vietnamese = new Set(existing.map((q) => viKey(q.vietnamese)));
  const counts = {}, selected = [];
  for (const type of Object.keys(TARGETS)) {
    const c = (counts[type] = { 'offered to the translator': 0, 'not rated (no answer)': 0, 'rated 0 (must not appear)': 0, 'rated 1 (not good enough alone)': 0, 'accepted, but no longer counted as the author\'s own sentence': 0, 'translation failed a text check': 0, 'duplicate or near-duplicate': 0, 'eligible for the check': 0, 'sent to the check': 0 });
    const list = rated.filter((r) => r.type === type);
    c['offered to the translator'] = list.length;
    const accepted = [];
    for (const r of list) {
      const a = answer.get(r.n);
      if (!a) { c['not rated (no answer)']++; continue; }
      if (a.useful === 0) { c['rated 0 (must not appear)']++; continue; }
      if (a.useful < 2) { c['rated 1 (not good enough alone)']++; continue; }
      if (!standalone.has(`${r.file}|${r.text}`)) { c['accepted, but no longer counted as the author\'s own sentence']++; continue; }
      accepted.push({ ...r, useful: a.useful, vi: (a.vi ?? '').normalize('NFC').replace(/\s+/g, ' ').trim() });
    }
    accepted.sort((x, y) => y.useful - x.useful || y.score - x.score || x.n - y.n);
    const eligible = [], kept = [];
    for (const r of accepted) {
      if (fault(r.vi)) { c['translation failed a text check']++; continue; }
      const words = tokens(r.text);
      if (english.has(key(r.text)) || vietnamese.has(viKey(r.vi)) || kept.some((k) => alike(k, words))) { c['duplicate or near-duplicate']++; continue; }
      english.add(key(r.text)); vietnamese.add(viKey(r.vi)); kept.push(words);
      eligible.push(r);
    }
    c['eligible for the check'] = eligible.length;
    const sent = eligible.slice(0, Math.ceil(TARGETS[type] * RESERVE));
    c['sent to the check'] = sent.length;
    selected.push(...sent);
  }
  writeFileSync(join(work, 'selected.jsonl'), jsonl(selected), 'utf8');
  writeFileSync(join(work, 'select-counts.json'), JSON.stringify(counts, null, 1), 'utf8');
  mkdirSync(join(work, 'check'), { recursive: true });
  const size = 250;
  for (let b = 0; b * size < selected.length; b++) {
    const part = selected.slice(b * size, (b + 1) * size), nn = String(b + 1).padStart(2, '0');
    writeFileSync(join(work, 'check', `back-${nn}.jsonl`), jsonl(part.map((r) => ({ n: r.n, vi: r.vi }))), 'utf8');
    writeFileSync(join(work, 'check', `ref-${nn}.jsonl`), jsonl(part.map((r) => ({ n: r.n, kind: r.type, author: r.author, work: r.work, text: r.text }))), 'utf8');
  }
  console.table(counts);
  console.log(`${selected.length} selected, ${Math.ceil(selected.length / size)} check batches`);
}

function random(seed) { let s = seed >>> 0; return () => { s = (s + 0x6d2b79f5) >>> 0; let t = Math.imul(s ^ (s >>> 15), 1 | s); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

function write() {
  const selected = lines(join(work, 'selected.jsonl'));
  const verdict = answers(join(work, 'check'), /^verdict-.*\.jsonl$/);
  const unchecked = selected.filter((r) => !['ok', 'doubtful', 'wrong'].includes(verdict.get(r.n)?.verdict));
  if (unchecked.length) { console.error(`${unchecked.length} selected items have no verdict (first n: ${unchecked.slice(0, 10).map((r) => r.n)}): nothing written`); process.exit(1); }
  const selectCounts = JSON.parse(readFileSync(join(work, 'select-counts.json'), 'utf8'));
  const candidateCounts = JSON.parse(readFileSync(join(work, 'candidates-counts.json'), 'utf8'));
  const next = random(20261126);
  const final = {}, checkCounts = {}, review = [];
  mkdirSync('Assets/Quotes/data', { recursive: true });
  mkdirSync('Assets/Quotes/review', { recursive: true });
  for (const type of Object.keys(TARGETS)) {
    const list = selected.filter((r) => r.type === type).map((r) => ({ ...r, check: verdict.get(r.n) }));
    const foreign = list.filter((r) => r.check.foreign === true);
    const wrong = list.filter((r) => r.check.foreign !== true && r.check.verdict === 'wrong');
    const kept = list.filter((r) => r.check.foreign !== true && r.check.verdict !== 'wrong');
    final[type] = kept.slice(0, TARGETS[type]);
    checkCounts[type] = {
      checked: list.length, 'looks like another author\'s words (left out)': foreign.length, 'wrong translation (left out)': wrong.length, 'above the target (left out)': kept.length - final[type].length,
      shipped: final[type].length, 'shipped and flagged doubtful': final[type].filter((r) => r.check.verdict === 'doubtful').length,
    };
    const records = final[type].map((r) => {
      const book = WORKS.find((w) => w.file === r.file);
      const translated = book.translator ? `, translated by ${book.translator}` : '';
      return {
        id: `pd:${type}:${r.file}:${createHash('sha256').update(key(r.text)).digest('hex').slice(0, 10)}`, type, lang: 'en', text: r.text,
        author: { name: book.author, died: book.died, ...(book.translator ? { translator: book.translator, translator_died: book.translatorDied } : {}) },
        attribution: { status: 'verified', work: book.work, year: book.year, evidence: 'the sentence stands word for word in the text of this work' },
        translations: [{ lang: 'vi', text: r.vi, status: 'machine_unreviewed', by: model, date, license: 'CC0-1.0' }],
        ...(r.check.verdict === 'doubtful' ? { flags: ['back_translation_disagrees'] } : {}),
        source: {
          dataset: 'Public-domain text of the author\'s work', url: `https://www.wikidata.org/wiki/${book.wikidata}`, retrieved: date, license: 'Public-Domain',
          attribution_text: `${book.author}, ${book.work} (${book.year})${translated}. Vietnamese made for PomoQuote, CC0 1.0`,
        },
      };
    }).sort((a, b) => a.id.localeCompare(b.id, 'en'));
    writeFileSync(`Assets/Quotes/data/public-domain-${NAMES[type]}.jsonl`, jsonl(records), 'utf8');
    const pool = [...final[type]], sample = [];
    while (sample.length < 50 && pool.length) sample.push(...pool.splice(Math.floor(next() * pool.length), 1));
    const inSample = new Set(sample.map((r) => r.n));
    review.push(...sample.map((r) => ['random sample', r]),
      ...final[type].filter((r) => r.check.verdict === 'doubtful' && !inSample.has(r.n)).map((r) => ['flagged by the check', r]),
      ...wrong.map((r) => ['left out: check says wrong', r]), ...foreign.map((r) => ['left out: may be another author', r]));
  }
  const cell = (v) => `"${String(v ?? '').replace(/"/g, '""')}"`;
  const header = ['why listed', 'type', 'English', 'author', 'work', 'Vietnamese', 'back-translation', 'check', 'check note', 'your verdict'];
  const rows = review.map(([why, r]) => [why, r.type, r.text, r.author, r.work, r.vi, r.check.back, r.check.verdict, r.check.note, '']);
  writeFileSync('Assets/Quotes/review/public-domain-review-sample.csv', '﻿' + [header, ...rows].map((r) => r.map(cell).join(',')).join('\r\n') + '\r\n', 'utf8');

  const table = (counts) => ['| Step | Quotes | Life maxims |', '| --- | ---: | ---: |', ...Object.keys(counts.quote).map((name) => `| ${name} | ${counts.quote[name]} | ${counts.maxim[name]} |`)].join('\n');
  const authors = [...new Set(WORKS.map((w) => w.author))];
  const bookRows = candidateCounts.map((c) => {
    const book = WORKS.find((w) => w.file === c.file);
    const shipped = final[c.type].filter((r) => r.file === c.file).length;
    return `| ${c.author} (${book.died < 0 ? `${-book.died} BC` : `d. ${book.died}`})${book.translator ? `, tr. ${book.translator} (d. ${book.translatorDied})` : ''} | ${c.work} (${book.year}) | ${c.file} | ${c.type} | ${c.pieces} | ${c.standalone} | ${c.offered} | ${shipped} |`;
  });
  writeFileSync('Assets/Quotes/public-domain-import-report.md', `# Public-domain quotes and life maxims: import report

Generated by \`tools/public-domain-candidates.mjs\` and \`tools/public-domain-build.mjs\`, ${date}. ${authors.length} authors, all dead in 1955 or before (as are the translators);
death dates checked against Wikidata. Vietnamese: ${model}, ${date}, checked by a second, independent model run. No person has reviewed it.

## 1. Books

\`Book\` is the ebook number of the plain-text file used (see \`tools/public-domain-works.mjs\`). Sentences: all pieces of the text; "alone" those that
pass the automatic rules (35 to 130 characters, a whole sentence, readable on its own, not quoted from somebody else); at most 170 per book were offered
(all of them for the numbered maxims, calendar sayings and the Analects).

| Author | Work | Book | Type | Sentences | Alone | Offered | Shipped |
| --- | --- | ---: | --- | ---: | ---: | ---: | ---: |
${bookRows.join('\n')}

## 2. Choice and translation

${table(selectCounts)}

## 3. Check and result

${table(checkCounts)}

Target: quotes 600, life maxims 600. Shipped: **${final.quote.length + final.maxim.length}** (quotes ${final.quote.length}, life maxims ${final.maxim.length}). Nothing is added to reach a target.

Review sample for the owner: [review/public-domain-review-sample.csv](review/public-domain-review-sample.csv).
`, 'utf8');
  console.table(checkCounts);
  console.log(`shipped ${final.quote.length + final.maxim.length}; review rows ${rows.length}`);
}

if (command === 'select') select(); else write();
