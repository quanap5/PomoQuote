#!/usr/bin/env node
/**
 * Step 1 of the quotes and life maxims import: takes candidate sentences out of the public-domain books listed in public-domain-works.mjs.
 * Development tool, run by hand on downloaded files; nothing here runs in the app.
 *
 *   node tools/public-domain-candidates.mjs --dir <folder with pg<n>.txt> --out <work folder>
 *
 * A candidate is a whole sentence found word for word in the author's own text (italics marks and footnote numbers removed, typography reduced
 * to plain characters), that can stand on its own. Sentences that quote somebody are left out of the prose books, because the words would not
 * be the author's. Step 2 (choice and translation) and the rest: tools/public-domain-build.mjs.
 */
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { WORKS } from './public-domain-works.mjs';
import { blockedEnglish } from './expression-content-filter.mjs';

const args = process.argv.slice(2);
const option = (name) => { const i = args.indexOf(`--${name}`); return i >= 0 && args[i + 1] ? args[i + 1] : undefined; };
const dir = option('dir'), out = option('out');
if (!dir || !out) { console.error('Usage: node tools/public-domain-candidates.mjs --dir <folder with the book files> --out <work folder>'); process.exit(2); }

const RULES = { min: 35, max: 130, perWork: 170 };
const typography = [[/[‘’‚‛′´`]/g, "'"], [/[“”„‟″«»]/g, '"'], [/—|―/g, ' - '], [/[–−]/g, '-'], [/…/g, '...'], [/[   ​]/g, ' '], [/æ/g, 'ae'], [/œ/g, 'oe'], [/Æ/g, 'Ae']];
const clean = (text) => {
  let t = text;
  for (const [re, to] of typography) t = t.replace(re, to);
  return t.replace(/\{\d+\}|\[\d+\]|\[[A-Za-z]\]/g, '').replace(/_/g, '').replace(/--/g, ' - ').replace(/\s+/g, ' ').trim();
};
const key = (text) => text.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();

/** Paragraphs of the line range; verse (deeply indented blocks) is left out except in plays. */
function paragraphs(work, lines) {
  const result = [];
  let block = [], skipping = false;
  const flush = () => {
    if (block.length) {
      // an indented block of a prose book is verse or a passage quoted from somewhere else
      const indented = work.mode === 'prose' && block.filter((l) => /^ {2,}/.test(l)).length > block.length / 2;
      if (!indented) result.push(block.map((l) => l.trim()).join(' '));
    }
    block = [];
  };
  for (const line of lines.slice(work.from - 1, work.to)) {
    if (work.skip) { // sections written by someone else (an editor's notes) lie between these two headings
      if (work.skip[0].test(line.trim())) { flush(); skipping = true; continue; }
      if (skipping && work.skip[1].test(line.trim())) skipping = false;
      if (skipping) continue;
    }
    if (!line.trim()) flush(); else block.push(line);
  }
  flush();
  return result;
}

// Quoted spans, by kind of quotation mark; each also matches a mark that is never closed, up to the end of the paragraph.
const QUOTED = [
  /“[^”]*(”|$)/g,
  /‘.*?(’(?![A-Za-z])|$)/g,
  /"[^"]*("|$)/g,
  /(?<=^|[\s(-])'(?!(Tis|Twas|Twere|Twill|T is)\b)(?=[A-Z]).*?((?<![s\s])'(?![A-Za-z])|$)/g,
];
/** A line signed with a name ("... .--MIRABEAU.") is an epigraph or a saying quoted from that person. */
const SIGNED = /[.!?"']\s*-{1,2}\s*[A-Z][A-Za-z.' ]{1,40}\.?\s*$/;

/**
 * The parts of a paragraph outside quotation marks. In a book that is not a story, what is quoted is somebody else's words, also the sentences
 * in the middle of a long quotation, which carry no mark of their own.
 */
const unquoted = (paragraph) => QUOTED.reduce((text, span) => text.replace(span, ' | '), paragraph).split('|');

const sentences = (text) => text
  .replace(/\b(Mr|Mrs|Dr|St|Rev|Hon|Col|Gen|Capt|Messrs|viz|i\.e|e\.g|etc|vol|No)\./g, '$1․')
  .split(/(?<=[.!?]["']?)\s+(?=["']?[A-Z])/)
  .map((s) => s.replace(/․/g, '.').trim());

/** The pieces of one paragraph that may be candidates, by the kind of book. */
function pieces(work, paragraph, previous) {
  if (work.mode === 'numbered') {
    const m = clean(paragraph).match(/^(\d+)\.\s*-\s*(.*)$/);
    return m ? [m[2].replace(/\s*\[.*$/, '').trim()] : [];
  }
  if (work.mode === 'calendar') {
    // the saying stands before the signature "--Pudd'nhead Wilson's (New) Calendar", in the same paragraph or the one before
    const text = clean(paragraph);
    const at = text.search(/-\s*Pudd'?nhead Wilson'?s (New )?Calendar/i);
    if (at < 0) return [];
    const saying = text.slice(0, at).replace(/[\s-]+$/, '').trim() || clean(previous ?? '');
    return saying.length <= RULES.max ? [saying] : sentences(saying);
  }
  if (work.mode === 'master') {
    const text = clean(paragraph);
    // only what stands inside the Master's own quotation marks: what follows them is the narrator or somebody else
    const m = text.match(/The Master said,\s*'(.+?)(?:'(?=\s|$)|$)/);
    return m ? sentences(m[1].trim()) : [];
  }
  if (work.mode === 'play') {
    const speech = clean(paragraph).replace(/\[[^\]]*\]/g, ' ').replace(/\([^)]*\)/g, ' ');
    const m = speech.match(/^[A-Z][A-Z .']{2,40}\.\s+(.*)$/);
    return m ? sentences(m[1].replace(/\s+/g, ' ')) : [];
  }
  if (!work.fiction && SIGNED.test(clean(paragraph))) return [];
  const parts = work.fiction ? [paragraph] : unquoted(paragraph);
  return parts.flatMap((part) => sentences(clean(part).replace(/^\d+\.\s+|^[IVXL]+\.\s+/, '')));
}

const OPENERS = /^(He|She|It was|They|This|These|Those|Thus|Hence|Such|His|Her|Their|Here|Then|But|And|So|Yet|Now|Again|Besides|Also|Nor|Or|For|However|Therefore|Indeed|Accordingly|Moreover|Still|Nevertheless|That is|There they|I was|I had|I have been|My|Which|Whereupon|Consequently|Another|Secondly|Lastly|Finally|Yes|No,|Oh|Ah|Well,)\b/;
const GENERAL_OPENERS = /^(He (who|that)|She who|They (who|that)|Those (who|that)|There is (no|nothing)|It is)\b/;
const NARRATIVE = /\b(was|were|had|said|did|went|came|replied|asked|cried|told|saw|took|gave|began|looked|thought that|answered)\b/;
const WISDOM = /\b(life|live|love|friend|friends|truth|true|mind|heart|happiness|happy|work|time|wisdom|wise|virtue|world|man|men|woman|people|nature|soul|good|great|learn|success|hope|fear|courage|habit|character|thought|think|book|books|young|youth|old|age|day|nothing|never|always|every|must|should|who|we|our|one|knowledge|fortune|fault|kind|kindness|patience|effort|will)\b/gi;

/** Whether a sentence can stand on its own, and how promising it looks. */
function judge(work, sentence) {
  const s = sentence;
  if (s.length < RULES.min || s.length > RULES.max) return null;
  if (!/^[\x20-\x7E]+$/.test(s) || !/^[A-Z]/.test(s) || !/[.!?]$/.test(s)) return null;
  if (/["\[\]{}()<>*#@|\\=~^]|\.\.|\d|\s'|'\s|^'/.test(s.replace(/s' /g, 's '))) return null; // quoted words, notes, numbers
  if (/\b[A-Z]{2,}\b/.test(s)) return null;
  const aphorism = work.mode === 'numbered' || work.mode === 'calendar' || work.mode === 'master';
  if (!aphorism) {
    if (OPENERS.test(s) && !GENERAL_OPENERS.test(s)) return null;
    // a name in the middle of a sentence makes it a piece of a story or of an argument about someone
    if (/(?<=[a-z,;:] )(?!I\b|God\b|Nature\b|Heaven\b|Truth\b)[A-Z][a-z]+/.test(s)) return null;
    if (NARRATIVE.test(s)) return null;
  }
  if (/\b(thou|thee|thy|thine|hath|doth|ye|unto|wherefore|hast|dost|shalt)\b/i.test(s)) return null; // too old-fashioned for a learner
  if (blockedEnglish(s)) return null;
  const hits = (s.match(WISDOM) ?? []).length;
  if (!aphorism && hits === 0) return null;
  return Math.min(hits, 5) * 2 - Math.abs(s.length - 75) / 20;
}

const candidates = [], counts = [], standalone = [];
const seen = new Set();
for (const work of WORKS) {
  const bytes = readFileSync(join(dir, `pg${work.file}.txt`));
  const hash = createHash('sha256').update(bytes).digest('hex');
  if (hash !== work.sha256) { console.error(`pg${work.file}.txt is not the file the line ranges belong to (sha256 ${hash})`); process.exit(1); }
  if (work.died > 1955 || (work.translatorDied ?? 0) > 1955) { console.error(`${work.author}: not in the public domain`); process.exit(1); }
  const paras = paragraphs(work, bytes.toString('utf8').split(/\r?\n/));
  const all = paras.flatMap((p, i) => pieces(work, p, paras[i - 1]));
  const good = [];
  for (const sentence of all) {
    const score = judge(work, sentence);
    if (score === null || seen.has(key(sentence))) continue;
    seen.add(key(sentence));
    good.push({ sentence, score });
  }
  good.sort((a, b) => b.score - a.score || a.sentence.localeCompare(b.sentence, 'en'));
  standalone.push(...good.map((g) => ({ file: work.file, text: g.sentence })));
  const keep = work.mode === 'numbered' || work.mode === 'calendar' || work.mode === 'master' ? good : good.slice(0, RULES.perWork);
  counts.push({ file: work.file, author: work.author, work: work.work, type: work.type, pieces: all.length, standalone: good.length, offered: keep.length });
  for (const { sentence, score } of keep) {
    candidates.push({ type: work.type, text: sentence, score: Math.round(score * 100) / 100, file: work.file, author: work.author, died: work.died, wikidata: work.wikidata, translator: work.translator, work: work.work, year: work.year });
  }
}

mkdirSync(out, { recursive: true });
writeFileSync(join(out, 'candidates.jsonl'), candidates.map((c, i) => JSON.stringify({ n: i + 1, ...c })).join('\n') + '\n', 'utf8');
writeFileSync(join(out, 'candidates-counts.json'), JSON.stringify(counts, null, 1), 'utf8');
writeFileSync(join(out, 'standalone.jsonl'), standalone.map((c) => JSON.stringify(c)).join('\n') + '\n', 'utf8'); // every sentence that qualifies
console.table(counts);
console.log(`${candidates.length} candidates: ${candidates.filter((c) => c.type === 'quote').length} quotes, ${candidates.filter((c) => c.type === 'maxim').length} maxims`);
