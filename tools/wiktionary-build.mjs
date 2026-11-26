#!/usr/bin/env node
/**
 * Steps 2 to 4 of the Wiktionary import (step 1 is wiktionary-candidates.mjs). Development tool, run by hand; nothing here runs in the app.
 *
 * Step 2, translation (done outside this script, by a language model): the candidates are handed over in files
 *   <work>/batches/in-NN.jsonl and come back as out-NN.jsonl, one line per candidate:
 *   {"n", "s": chosen meaning, "useful": 0-3, "why", "vi", "src": true when vi is the dictionary's own translation, "ex_vi"}
 *   <work>/picked.jsonl holds the candidates with their number n.
 * Step 3: node tools/wiktionary-build.mjs select --work <work>
 *   validates the translations, removes duplicates and writes <work>/selected.jsonl plus the files for the back-translation check
 *   (<work>/check/back-NN.jsonl holds only the Vietnamese; ref-NN.jsonl the English). A second, independent model run translates the
 *   Vietnamese back without seeing the English, then compares, and writes verdict-NN.jsonl: {"n", "verdict": "ok"|"doubtful"|"wrong", "back", "note"}.
 * Step 4: node tools/wiktionary-build.mjs write --work <work>
 *   drops what the check calls wrong, flags the doubtful, writes Assets/Quotes/data/wiktionary-*.jsonl, the import report and the review sample.
 */
import { readFileSync, writeFileSync, readdirSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { blockedEnglish, blockedVietnamese, LIST_SIZES } from './expression-content-filter.mjs';

const [command, ...args] = process.argv.slice(2);
const option = (name, fallback) => { const i = args.indexOf(`--${name}`); return i >= 0 && args[i + 1] ? args[i + 1] : fallback; };
const work = option('work');
if (!['select', 'write'].includes(command) || !work) { console.error('Usage: node tools/wiktionary-build.mjs select|write --work <folder> [--date yyyy-mm-dd] [--model <name>]'); process.exit(2); }
const date = option('date', '2026-11-26');
const model = option('model', 'Claude Sonnet (Anthropic), machine draft');

const TARGETS = { proverb: 600, phrasal_verb: 800, expression: 800, idiom: 1500 };
const NAMES = { proverb: 'proverbs', phrasal_verb: 'phrasal-verbs', expression: 'expressions', idiom: 'idioms' };
const MIN_USEFUL = 2;
const RESERVE = 1.06; // checked beyond the target, to make up for what the check drops
const LIMITS = { vietnamese: 110, exampleVietnamese: 200 };

const lines = (path) => readFileSync(path, 'utf8').split(/\r?\n/).filter((l) => l.trim()).map((l) => JSON.parse(l));
const jsonl = (rows) => rows.map((r) => JSON.stringify(r)).join('\n') + '\n';
const key = (text) => text.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
const viKey = (text) => text.normalize('NFC').toLowerCase().replace(/[\s.,!?]+/g, ' ').trim();
const tidy = (text) => (text ?? '').normalize('NFC').replace(/[‘’]/g, "'").replace(/\s+/g, ' ').trim();
const diacritics = /[ăâđêôơưàáảãạằắẳẵặầấẩẫậèéẻẽẹềếểễệìíỉĩịòóỏõọồốổỗộờớởỡợùúủũụừứửữựỳýỷỹỵ]/i;

/** Why a translation cannot ship, or '' when it can. */
function fault(text, headword) {
  if (text.length < 2 || text.length > LIMITS.vietnamese) return 'length';
  if (/[()\[\]{}<>"“”\/\\|_*#@=~^]|\.\.|;/.test(text)) return 'markup, brackets or several alternatives';
  if (!/^[\p{Script=Latin}\p{M}\d\s,'.!?:%-]+$/u.test(text)) return 'unexpected characters';
  if (text.split(' ').length >= 3 && !diacritics.test(text)) return 'no Vietnamese diacritics';
  if (viKey(text) === viKey(headword)) return 'copy of the English';
  if (blockedVietnamese(text)) return 'blocked word';
  return '';
}

function select() {
  const picked = new Map(lines(join(work, 'picked.jsonl')).map((c) => [c.n, c]));
  const answers = new Map();
  for (const file of readdirSync(join(work, 'batches')).filter((f) => /^out-.*\.jsonl$/.test(f)).sort()) for (const o of lines(join(work, 'batches', file))) answers.set(o.n, o);
  const quotes = JSON.parse(readFileSync('Assets/Quotes/quotes.json', 'utf8')).groups.flatMap((g) => g.quotes);
  const english = new Set(quotes.map((q) => key(q.english))), vietnamese = new Set(quotes.map((q) => viKey(q.vietnamese)));
  const counts = {};
  const selected = [];
  for (const type of Object.keys(TARGETS)) {
    const c = (counts[type] = { translated: 0, 'rated 0 (do not ship)': 0, 'rated 1 (rare or specialised)': 0, 'translation failed a text check': 0, 'duplicate English or Vietnamese': 0, 'eligible for the check': 0, 'sent to the check': 0 });
    const list = [...picked.values()].filter((p) => p.type === type && answers.has(p.n)).map((p) => ({ p, a: answers.get(p.n) }));
    c.translated = list.length;
    list.sort((x, y) => y.a.useful - x.a.useful || y.p.score - x.p.score || x.p.n - y.p.n);
    const eligible = [];
    for (const { p, a } of list) {
      if (a.useful === 0) { c['rated 0 (do not ship)']++; continue; }
      if (a.useful < MIN_USEFUL) { c['rated 1 (rare or specialised)']++; continue; }
      const sense = p.senses[a.s];
      const vi = tidy(a.vi).replace(/(?<![.!?])[.]$/, '');
      if (!sense || fault(vi, p.text) || blockedEnglish(`${p.text} ${sense.meaning}`)) { c['translation failed a text check']++; continue; }
      if (english.has(key(p.text)) || vietnamese.has(viKey(vi))) { c['duplicate English or Vietnamese']++; continue; }
      english.add(key(p.text)); vietnamese.add(viKey(vi));
      const source = Boolean(a.src) && tidy(sense.sourceVi) === tidy(a.vi);
      let exampleVi = source || !sense.example ? '' : tidy(a.ex_vi);
      if (exampleVi && (exampleVi.length > LIMITS.exampleVietnamese || blockedVietnamese(exampleVi) || !diacritics.test(exampleVi))) exampleVi = '';
      eligible.push({ n: p.n, id: p.id, type, text: p.text, url: p.url, meaning: sense.meaning, register: sense.register, example: sense.example, vi, exampleVi, source, useful: a.useful, score: p.score });
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
    writeFileSync(join(work, 'check', `back-${nn}.jsonl`), jsonl(part.map((r) => ({ n: r.n, vi: r.vi, ex_vi: r.exampleVi || undefined }))), 'utf8');
    writeFileSync(join(work, 'check', `ref-${nn}.jsonl`), jsonl(part.map((r) => ({ n: r.n, type: r.type, text: r.text, meaning: r.meaning, example: r.exampleVi ? r.example : undefined }))), 'utf8');
  }
  console.table(counts);
  console.log(`${selected.length} selected, ${Math.ceil(selected.length / size)} check batches`);
}

/** A small seeded generator, so the same input gives the same review sample. */
function random(seed) { let s = seed >>> 0; return () => { s = (s + 0x6d2b79f5) >>> 0; let t = Math.imul(s ^ (s >>> 15), 1 | s); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

function write() {
  const selected = lines(join(work, 'selected.jsonl'));
  const verdicts = new Map();
  for (const file of readdirSync(join(work, 'check')).filter((f) => /^verdict-.*\.jsonl$/.test(f)).sort()) for (const o of lines(join(work, 'check', file))) verdicts.set(o.n, o);
  const unchecked = selected.filter((r) => !['ok', 'doubtful', 'wrong'].includes(verdicts.get(r.n)?.verdict));
  if (unchecked.length) { console.error(`${unchecked.length} selected items have no verdict (first n: ${unchecked.slice(0, 10).map((r) => r.n)}): nothing written`); process.exit(1); }
  const candidateCounts = JSON.parse(readFileSync(join(work, 'candidates-counts.json'), 'utf8'));
  const selectCounts = JSON.parse(readFileSync(join(work, 'select-counts.json'), 'utf8'));
  const final = {}, checkCounts = {}, review = [];
  const next = random(20261126);
  mkdirSync('Assets/Quotes/data', { recursive: true });
  mkdirSync('Assets/Quotes/review', { recursive: true });
  for (const type of Object.keys(TARGETS)) {
    const list = selected.filter((r) => r.type === type).map((r) => ({ ...r, check: verdicts.get(r.n) }));
    const kept = list.filter((r) => r.check.verdict !== 'wrong');
    final[type] = kept.slice(0, TARGETS[type]);
    checkCounts[type] = {
      checked: list.length, ok: list.filter((r) => r.check.verdict === 'ok').length, doubtful: list.filter((r) => r.check.verdict === 'doubtful').length,
      'wrong (left out)': list.length - kept.length, 'above the target (left out)': kept.length - final[type].length, shipped: final[type].length,
      'shipped and flagged doubtful': final[type].filter((r) => r.check.verdict === 'doubtful').length, 'shipped with the dictionary\'s own Vietnamese': final[type].filter((r) => r.source).length,
    };
    const records = [...final[type]].sort((a, b) => a.id.localeCompare(b.id, 'en')).map((r) => ({
      id: r.id, type, lang: 'en', text: r.text,
      meanings: [{ lang: 'en', text: r.meaning, ...(r.register?.length ? { register: r.register } : {}) }],
      translations: [{ lang: 'vi', text: r.vi, status: r.source ? 'source' : 'machine_unreviewed', by: r.source ? 'English Wiktionary contributors' : model, date, license: 'CC-BY-SA-4.0' }],
      ...(r.example ? { examples: [{ en: r.example, ...(r.exampleVi ? { vi: r.exampleVi } : {}) }] } : {}),
      ...(r.check.verdict === 'doubtful' ? { flags: ['back_translation_disagrees'] } : {}),
      source: {
        dataset: 'English Wiktionary (kaikki.org extract)', url: r.url, retrieved: date, license: 'CC-BY-SA-4.0',
        attribution_text: 'English Wiktionary contributors (en.wiktionary.org), selected and translated for PomoQuote',
      },
    }));
    writeFileSync(`Assets/Quotes/data/wiktionary-${NAMES[type]}.jsonl`, jsonl(records), 'utf8');
    // for the owner: 50 random shipped items, every shipped doubtful one, and what the check left out
    const pool = [...final[type]];
    const sample = [];
    while (sample.length < 50 && pool.length) sample.push(...pool.splice(Math.floor(next() * pool.length), 1));
    const inSample = new Set(sample.map((r) => r.n));
    review.push(...sample.map((r) => ['random sample', r]),
      ...final[type].filter((r) => r.check.verdict === 'doubtful' && !inSample.has(r.n)).map((r) => ['flagged by the check', r]),
      ...list.filter((r) => r.check.verdict === 'wrong').map((r) => ['left out: check says wrong', r]));
  }
  const cell = (v) => `"${String(v ?? '').replace(/"/g, '""')}"`;
  const header = ['why listed', 'type', 'English', 'meaning', 'Vietnamese', 'translation from', 'example', 'example Vietnamese', 'back-translation', 'check', 'check note', 'your verdict'];
  const rows = review.map(([why, r]) => [why, r.type, r.text, r.meaning, r.vi, r.source ? 'dictionary' : 'machine', r.example, r.exampleVi, r.check.back, r.check.verdict, r.check.note, '']);
  writeFileSync('Assets/Quotes/review/wiktionary-review-sample.csv', '﻿' + [header, ...rows].map((r) => r.map(cell).join(',')).join('\r\n') + '\r\n', 'utf8');

  const table = (counts) => {
    const types = Object.keys(TARGETS);
    const names = Object.keys(counts[types[0]] ?? counts);
    return ['| Step | Proverbs | Phrasal verbs | Expressions | Idioms |', '| --- | ---: | ---: | ---: | ---: |',
      ...names.map((name) => `| ${name} | ${types.map((t) => counts[t][name]).join(' | ')} |`)].join('\n');
  };
  const candidateTable = ['| Step | Proverbs | Phrasal verbs | Expressions | Idioms |', '| --- | ---: | ---: | ---: | ---: |',
    ...Object.entries(candidateCounts).map(([name, c]) => `| ${name} | ${c.proverb} | ${c.phrasal_verb} | ${c.expression} | ${c.idiom} |`)].join('\n');
  const total = Object.values(final).reduce((n, l) => n + l.length, 0);
  writeFileSync('Assets/Quotes/wiktionary-import-report.md', `# Wiktionary import report

Generated by \`tools/wiktionary-candidates.mjs\` and \`tools/wiktionary-build.mjs\` from the kaikki.org extract of English Wiktionary retrieved ${date}
(CC BY-SA 4.0). Vietnamese: ${model}, ${date}; checked by a second, independent model run that translated the Vietnamese back without seeing the
English. No person has reviewed the machine translations.

## 1. Candidates

${candidateTable}

Content rule: left out when the headword, the first meaning, any slur, or half of the meanings or more are offensive, vulgar or adult
(Wiktionary's tags and categories and a word list of ${LIST_SIZES.words} words and ${LIST_SIZES.stems} stems; Vietnamese list ${LIST_SIZES.vietnamese}). Death, drink and religion are not filtered.

## 2. Translation and selection

The best candidates by commonness were translated; the translator rated each one. Only ratings 2 and 3 are used.

${table(selectCounts)}

## 3. Back-translation check and result

${table(checkCounts)}

Target per type: ${Object.entries(TARGETS).map(([t, n]) => `${NAMES[t].replace('-', ' ')} ${n}`).join(', ')}. Shipped: **${total}** (${Object.entries(final).map(([t, l]) => `${NAMES[t].replace('-', ' ')} ${l.length}`).join(', ')}).
Nothing is added to reach a target; where the number is below it, the eligible entries ran out.

Review sample for the owner: [review/wiktionary-review-sample.csv](review/wiktionary-review-sample.csv) (50 random shipped items per type, every shipped item the check
calls doubtful, and every item it called wrong).
`, 'utf8');
  console.table(checkCounts);
  console.log(`shipped ${total}; review rows ${rows.length}`);
}

if (command === 'select') select(); else write();
