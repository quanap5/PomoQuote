#!/usr/bin/env node
/**
 * Step 1 of the Wiktionary import: picks the English idioms, proverbs, phrasal verbs and common expressions to translate. A development tool,
 * run by hand on downloaded files; nothing here ships with the app, and the app never calls the network.
 *
 *   1. Download from https://kaikki.org/dictionary/English/ (data of English Wiktionary, CC BY-SA 4.0 / GFDL) the part-of-speech JSON Lines
 *      files proverb, phrase, prep_phrase, intj, verb, adv, adj and noun, and save them as en-<name>.jsonl in one folder.
 *   2. Have Tatoeba's eng_sentences_detailed.tsv (see import-tatoeba.mjs) in a folder: how often an expression occurs in its 2 million English
 *      sentences is the measure of how common it is.
 *   3. node tools/wiktionary-candidates.mjs --dir <kaikki folder> --tatoeba <folder> --out <work folder> [--quotes Assets/Quotes/quotes.json]
 *
 * It writes <work folder>/candidates.jsonl (best first, per type) and candidates-counts.json (the number left after each step).
 * Step 2 is the translation (see tools/wiktionary-build.mjs).
 */
import { createReadStream, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { createInterface } from 'node:readline';
import { join, resolve } from 'node:path';
import { blockedEnglish, blockedVietnamese, OFFENSIVE_TAGS, BLOCKED_CATEGORY } from './expression-content-filter.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => { const i = args.indexOf(`--${name}`); return i >= 0 && args[i + 1] ? args[i + 1] : fallback; };
const dir = option('dir'), tatoeba = option('tatoeba'), out = option('out');
if (!dir || !tatoeba || !out) { console.error('Usage: node tools/wiktionary-candidates.mjs --dir <kaikki folder> --tatoeba <tatoeba folder> --out <work folder>'); process.exit(2); }
const quotesPath = resolve(option('quotes', 'Assets/Quotes/quotes.json'));

const RULES = { maxHeadword: 60, maxWords: 9, maxMeaning: 200, minExample: 12, maxExample: 100 };
/** Senses that are not for a learner: out of use, regional, or only a pointer to another word. */
const UNUSABLE = new Set(['obsolete', 'archaic', 'dated', 'rare', 'uncommon', 'historical', 'dialectal', 'nonstandard', 'form-of', 'alt-of', 'misspelling', 'no-gloss']);
const REGIONAL = new Set(['Scotland', 'Ireland', 'Northern-England', 'India', 'South-Africa', 'Appalachia', 'Hawaii', 'Singapore', 'Philippines', 'Jamaica', 'Nigeria', 'Northumbria', 'Yorkshire', 'Geordie', 'Caribbean', 'Pakistan', 'Hong-Kong', 'Malaysia']);
const FILES = ['proverb', 'phrase', 'prep_phrase', 'intj', 'verb', 'adv', 'adj', 'noun'];
const TYPES = ['proverb', 'phrasal_verb', 'expression', 'idiom'];

const typography = [[/[‘’‚‛′´`]/g, "'"], [/[“”„‟″«»]/g, '"'], [/[–—−]/g, '-'], [/…/g, '...'], [/[   ​]/g, ' ']];
const ascii = (text) => { let t = text; for (const [re, to] of typography) t = t.replace(re, to); return t.replace(/\s+/g, ' ').trim(); };
const plain = (text) => /^[\x20-\x7E]+$/.test(text) && !/[<>@#\\{}\[\]|_*~^=]|https?:|www\./i.test(text);
const key = (text) => text.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
const categories = (sense) => (sense.categories ?? []).map((c) => (typeof c === 'string' ? c : c.name)).filter(Boolean);
const isOffensive = (s) => (s.tags ?? []).some((t) => OFFENSIVE_TAGS.has(t)) || categories(s).some((c) => BLOCKED_CATEGORY.test(c)) || blockedEnglish((s.glosses ?? []).join(' ')) || blockedEnglish((s.topics ?? []).join(' '));
const gloss = (s) => ascii((s.glosses ?? []).at(-1) ?? '').replace(/\s*\([^()]*\)\s*$/, '').trim();
const usable = (s) => {
  const g = gloss(s);
  return g.length > 0 && !/^Used other than|^Synonym of|^Alternative |^See |^(Abbreviation|Initialism|Acronym|Contraction) of/i.test(g)
    && !(s.tags ?? []).some((t) => UNUSABLE.has(t) || REGIONAL.has(t));
};

/** A short usage example written for the dictionary (never a quotation from a book or a newspaper: those belong to someone else). */
function ownExample(sense, headword) {
  const head = key(headword).split(' ').filter((w) => w.length >= 3);
  for (const x of sense.examples ?? []) {
    if (x.type !== 'example' || x.ref || !x.text || x.english || x.translation) continue;
    const text = ascii(x.text);
    if (text.length < RULES.minExample || text.length > RULES.maxExample || !plain(text) || !/^["']?[A-Z]/.test(text) || !/[.!?]["']?$/.test(text) || /\.\.\./.test(text)) continue;
    if (blockedEnglish(text)) continue;
    if (head.length && !head.some((w) => key(text).includes(w.slice(0, 4)))) continue;
    return text;
  }
  return '';
}

/** A Vietnamese translation Wiktionary gives for this meaning, if it is a clean one; the translator step still has to confirm it fits. */
function sourceVietnamese(entry, sense) {
  const vi = (t) => (t.code ?? t.lang_code) === 'vi' && typeof t.word === 'string';
  const words = (text) => new Set(key(text ?? '').split(' ').filter((w) => w.length >= 4));
  const meaning = words((sense.glosses ?? []).join(' '));
  const single = (entry.senses ?? []).length === 1;
  const list = [
    ...(sense.translations ?? []).filter(vi),
    ...(entry.translations ?? []).filter(vi).filter((t) => single || [...words(t.sense)].filter((w) => meaning.has(w)).length >= 2),
  ];
  for (const t of list) {
    const text = t.word.normalize('NFC').replace(/\s+/g, ' ').trim();
    if (text.length >= 2 && text.length <= 80 && /^[\p{Script=Latin}\p{M}\s,'.!?-]+$/u.test(text) && !/\.\.|\s[,.]/.test(text)
      && /[ăâđêôơưàáảãạằắẳẵặầấẩẫậèéẻẽẹềếểễệìíỉĩịòóỏõọồốổỗộờớởỡợùúủũụừứửữựỳýỷỹỵ]/i.test(text) && !blockedVietnamese(text)) return text;
  }
  return '';
}

// ---------------------------------------------------------------- read the dictionary
const counts = {};
const count = (name, type, n = 1) => { counts[name] ??= Object.fromEntries(TYPES.map((t) => [t, 0])); counts[name][type] += n; };
const entries = new Map(); // lower-case headword -> what its parts of speech say
for (const pos of FILES) {
  for await (const line of createInterface({ input: createReadStream(join(dir, `en-${pos}.jsonl`)), crlfDelay: Infinity })) {
    if (!line) continue;
    const e = JSON.parse(line);
    if (e.lang_code !== 'en' || !/\s/.test(e.word ?? '')) continue;
    const senses = e.senses ?? [];
    if (!senses.length) continue;
    const k = e.word.toLowerCase();
    const entry = entries.get(k) ?? entries.set(k, { word: e.word, parts: [] }).get(k);
    entry.parts.push({ pos: e.pos, file: pos, word: e.word, translations: e.translations, senses });
  }
  console.log(`read ${pos}`);
}

/** The type of a headword and the meanings that qualify for it; one headword goes to one type only. */
function classify(entry) {
  const all = entry.parts.flatMap((part) => part.senses.map((sense) => ({ part, sense })));
  const pick = (type, filter) => { const found = all.filter(filter); return found.length ? { type, found } : null; };
  return pick('proverb', (x) => x.part.pos === 'proverb')
    ?? pick('phrasal_verb', (x) => x.part.pos === 'verb' && categories(x.sense).some((c) => c.startsWith('English phrasal verbs')))
    ?? (all.some((x) => (x.sense.tags ?? []).includes('idiomatic')) ? pick('idiom', (x) => (x.sense.tags ?? []).includes('idiomatic')) : null)
    ?? pick('expression', (x) => ['phrase', 'prep_phrase', 'intj'].includes(x.part.pos));
}

// ---------------------------------------------------------------- the steps
const existing = new Set(JSON.parse(readFileSync(quotesPath, 'utf8')).groups.flatMap((g) => g.quotes.map((q) => key(q.english))));
const candidates = [];
for (const entry of entries.values()) {
  const kind = classify(entry);
  if (!kind) continue;
  const { type, found } = kind;
  count('Multiword English headwords of this type', type);
  const word = ascii(entry.word);
  const n = word.split(' ').length;
  if (!plain(word) || word.length > RULES.maxHeadword || n > RULES.maxWords || /\d{3,}|^[A-Z .'-]+$/.test(word) || /^(do you speak|(i|we) (don't |do not )?speak|i('m| am) (a |an )?[A-Z])/i.test(word)) continue;
  count(`Headword: plain characters, at most ${RULES.maxHeadword} characters and ${RULES.maxWords} words, not a "do you speak X" phrasebook line`, type);
  const good = found.filter((x) => usable(x.sense));
  if (!good.length) continue;
  count('Has a meaning in current, general use (not obsolete, archaic, dated, rare, regional, nonstandard or only a pointer)', type);
  // offensive, vulgar or adult: the headword itself, its first meaning, any slur, or half of its meanings or more
  const every = entry.parts.flatMap((p) => p.senses);
  const bad = every.filter(isOffensive).length;
  if (blockedEnglish(word) || isOffensive(every[0]) || every.some((s) => (s.tags ?? []).includes('slur') || categories(s).some((c) => /slurs/i.test(c))) || bad * 2 >= every.length) continue;
  const clean = good.filter((x) => !isOffensive(x.sense) && gloss(x.sense).length <= RULES.maxMeaning && plain(gloss(x.sense)));
  if (!clean.length) continue;
  count('Not offensive, vulgar or adult, and a plain meaning of at most 200 characters', type);
  if (existing.has(key(word))) continue;
  count('Not already a sentence of the app', type);
  // up to four meanings, in the dictionary's order; the translator step picks the most useful one
  const senses = clean.slice(0, 4).map((x) => ({
    pos: x.part.pos,
    meaning: gloss(x.sense),
    register: (x.sense.tags ?? []).filter((t) => ['informal', 'slang', 'colloquial', 'formal', 'figuratively', 'humorous', 'euphemistic', 'British', 'US', 'literary'].includes(t)),
    example: ownExample(x.sense, word),
    sourceVi: sourceVietnamese(x.part, x.sense),
    page: x.part.word,
  }));
  candidates.push({
    id: `enwikt:${type}:${key(word).replace(/ /g, '-')}`,
    type,
    text: word,
    pos: senses[0].pos,
    senses,
    url: `https://en.wiktionary.org/wiki/${encodeURIComponent(senses[0].page.replace(/ /g, '_')).replace(/%2C/g, ',')}`,
  });
}

// ---------------------------------------------------------------- how common: occurrences in Tatoeba's English sentences
const IRREGULAR = Object.fromEntries(`arise arose arisen,awake awoke awoken,bear bore borne,beat beaten,become became,begin began begun,bend bent,bind bound,bite bit bitten,bleed bled,blow blew blown,break broke broken,bring brought,build built,burn burnt,buy bought,cast,catch caught,choose chose chosen,cling clung,come came,creep crept,deal dealt,dig dug,do does did done,draw drew drawn,drink drank drunk,drive drove driven,dwell dwelt,eat ate eaten,fall fell fallen,feed fed,feel felt,fight fought,find found,flee fled,fling flung,fly flew flown,forget forgot forgotten,freeze froze frozen,get got gotten,give gave given,go goes went gone,grind ground,grow grew grown,hang hung,have has had,hear heard,hide hid hidden,hold held,keep kept,kneel knelt,know knew known,lay laid,lead led,leave left,lend lent,lie lay lain lying,light lit,lose lost,make made,mean meant,meet met,pay paid,ride rode ridden,ring rang rung,rise rose risen,run ran,say said,see saw seen,seek sought,sell sold,send sent,shake shook shaken,shine shone,shoot shot,show shown,shrink shrank shrunk,sing sang sung,sink sank sunk,sit sat,sleep slept,slide slid,sling slung,speak spoke spoken,speed sped,spend spent,spin spun,spit spat,spring sprang sprung,stand stood,steal stole stolen,stick stuck,sting stung,strike struck,string strung,swear swore sworn,sweep swept,swim swam swum,swing swung,take took taken,teach taught,tear tore torn,tell told,think thought,throw threw thrown,tie tied tying,die died dying,wake woke woken,wear wore worn,weave wove woven,weep wept,win won,wind wound,wring wrung,write wrote written`
  .split(',').map((line) => { const [base, ...forms] = line.split(' '); return [base, forms]; }));
function forms(candidate) {
  const phrase = candidate.text.toLowerCase();
  if (candidate.pos !== 'verb') return [phrase];
  const [v, ...r] = phrase.split(' ');
  const rest = r.join(' ');
  const stem = v.replace(/e$/, '');
  const verbs = [v, v + 's', v + 'es', v + 'ed', v + 'd', stem + 'ed', stem + 'ing', v + 'ing', v + v.slice(-1) + 'ed', v + v.slice(-1) + 'ing', v.replace(/y$/, 'ies'), v.replace(/y$/, 'ied'), ...(IRREGULAR[v] ?? [])];
  return [...new Set(verbs.map((x) => `${x} ${rest}`))];
}
const wanted = new Map();
for (const c of candidates) for (const f of forms(c)) { const k = (f.match(/[a-z']+/g) ?? []).join(' '); (wanted.get(k) ?? wanted.set(k, []).get(k)).push(c); }
for (const c of candidates) c.count = 0;
for await (const line of createInterface({ input: createReadStream(join(tatoeba, 'eng_sentences_detailed.tsv')), crlfDelay: Infinity })) {
  const text = line.split('\t')[2];
  if (!text) continue;
  const w = text.toLowerCase().match(/[a-z']+/g) ?? [];
  const seen = new Set();
  for (let n = 2; n <= 9; n++) for (let i = 0; i + n <= w.length; i++) for (const c of wanted.get(w.slice(i, i + n).join(' ')) ?? []) seen.add(c);
  for (const c of seen) c.count++;
}

// worth a learner's time: seen in real sentences, or the dictionary shows how to use it, or it already has a Vietnamese translation (proverbs: all)
const has = (c, field) => c.senses.some((s) => s[field]);
const worthy = candidates.filter((c) => c.type === 'proverb' || c.count >= 1 || has(c, 'example') || has(c, 'sourceVi'));
for (const c of worthy) count('Occurs in a Tatoeba sentence, or has an own example or a Vietnamese translation (proverbs: all)', c.type);
// best first: commonness, then a translation or an example; compound nouns after other idioms
for (const c of worthy) c.score = Math.round((Math.log2(1 + c.count) * 2 + (has(c, 'sourceVi') ? 4 : 0) + (has(c, 'example') ? 2 : 0) - (c.pos === 'noun' ? 3 : 0)) * 100) / 100;
worthy.sort((a, b) => TYPES.indexOf(a.type) - TYPES.indexOf(b.type) || b.score - a.score || a.text.localeCompare(b.text));
const ids = new Set();
const unique = worthy.filter((c) => !ids.has(c.id) && ids.add(c.id));
for (const c of unique) count('Unique id: one entry for spellings that differ only in punctuation or capitals', c.type);

mkdirSync(out, { recursive: true });
writeFileSync(join(out, 'candidates.jsonl'), unique.map((c) => JSON.stringify(c)).join('\n') + '\n', 'utf8');
writeFileSync(join(out, 'candidates-counts.json'), JSON.stringify(counts, null, 1), 'utf8');
console.table(counts);
console.log(TYPES.map((t) => `${t}: ${unique.filter((c) => c.type === t).length} candidates, ${unique.filter((c) => c.type === t && has(c, 'sourceVi')).length} with Vietnamese, ${unique.filter((c) => c.type === t && has(c, 'example')).length} with example`).join('\n'));
