#!/usr/bin/env node
/**
 * Builds the Tatoeba group of Assets/Quotes/quotes.json from Tatoeba's public exports. It is a development tool: it is run by hand, offline
 * after the files are downloaded, and nothing here ships with the app or runs at runtime (the app never calls the network).
 *
 *   1. Download these files from https://downloads.tatoeba.org/exports/per_language/ and decompress them (Git for Windows ships
 *      `bzip2 -dk file.bz2`; 7-Zip also works) into one folder:
 *        vie/vie-eng_links.tsv.bz2          (pairs: Vietnamese sentence id, English sentence id)
 *        eng/eng_sentences_detailed.tsv.bz2 (id, lang, text, author, added, modified)
 *        vie/vie_sentences_detailed.tsv.bz2
 *        eng/eng_tags.tsv.bz2, vie/vie_tags.tsv.bz2 (sentence id, tag)
 *   2. node tools/import-tatoeba.mjs --dir <that folder> [--quotes Assets/Quotes/quotes.json] [--report Assets/Quotes/tatoeba-import-report.md]
 *
 * It keeps every group that is already in quotes.json byte for byte, replaces a previous Tatoeba group (always the last one) and writes the
 * new one, then writes a report with the number of pairs left after each step. Tatoeba text is licensed CC BY 2.0 FR: the id and the author
 * of each English sentence and of its Vietnamese translation are written into each entry (see Assets/Quotes/CREDITS.md). Besides typography
 * (typographic quotes and dashes become plain ASCII; whitespace is collapsed; Vietnamese is normalised to NFC) no wording is changed.
 *
 * The rules are listed in the report and in the RULES block below; change a rule here, run the tool again, and read the new counts.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

// ---------------------------------------------------------------- options
const args = process.argv.slice(2);
const option = (name, fallback) => { const i = args.indexOf(`--${name}`); return i >= 0 && args[i + 1] ? args[i + 1] : fallback; };
const dir = option('dir');
if (!dir) { console.error('Usage: node tools/import-tatoeba.mjs --dir <folder with the decompressed exports> [--quotes <file>] [--report <file>] [--exports-date <yyyy-mm-dd>]'); process.exit(2); }
const quotesPath = resolve(option('quotes', 'Assets/Quotes/quotes.json'));
const reportPath = resolve(option('report', 'Assets/Quotes/tatoeba-import-report.md'));
const exportsDate = option('exports-date', '2026-11-21');

// ---------------------------------------------------------------- RULES
const RULES = {
  maxEnglishChars: Number(option('max-english', 100)),      // the widget shows a sentence in at most three lines at its smallest size
  maxVietnameseChars: Number(option('max-vietnamese', 130)),
  minEnglishChars: 12,
  minEnglishWords: 3,
  maxEnglishWords: 20,
  lengthRatio: [0.55, 2.2],                                  // Vietnamese characters / English characters of a faithful pair
  wordRatio: [0.6, 2.4],                                     // Vietnamese syllables / English words
  maxDigitsPerSentence: 6,
};
// Tags of Tatoeba users on a sentence that mean "not safe to reuse": needs a change, is ambiguous, is not literal, is vulgar or slang.
const EXCLUDED_TAG = /vulgar|offensive|swear|slang|nsfw|insult|@?change|@?check|ambig|wrong|incorrect|error|typo|needs|non-literal|dialect of|sexual|adult/i;

const ENGLISH_BLOCK = [
  // adult and sexual
  'sex', 'sexy', 'sexual', 'sexually', 'sexuality', 'porn', 'porno', 'pornography', 'naked', 'nude', 'nudity', 'rape', 'raped', 'rapist', 'prostitute', 'prostitutes', 'prostitution',
  'whore', 'slut', 'bitch', 'bitches', 'fuck', 'fucked', 'fucking', 'shit', 'shitty', 'dick', 'penis', 'vagina', 'boob', 'boobs', 'breast', 'breasts', 'nipple', 'nipples', 'orgasm',
  'masturbate', 'masturbation', 'erotic', 'horny', 'gay', 'lesbian', 'homosexual', 'homosexuality', 'transgender', 'abortion', 'condom', 'condoms', 'virgin', 'adultery', 'mistress',
  'affair', 'lover', 'lovers', 'kiss', 'kissed', 'kisses', 'kissing', 'bastard', 'damn', 'damned', 'hell', 'ass', 'asshole', 'crap', 'hooker', 'stripper', 'seduce', 'seduced', 'pregnant',
  'pregnancy', 'bra', 'underwear', 'panties', 'sperm', 'cock', 'pussy', 'balls', 'testicle', 'testicles', 'toilet', 'fart', 'farts', 'poop', 'pee', 'vomit', 'urine', 'sewage',
  // insults
  'idiot', 'idiots', 'moron', 'stupid', 'dumb', 'jerk', 'fool', 'fools', 'loser', 'ugly', 'fat', 'retard', 'retarded', 'crazy', 'insane', 'hate', 'hated', 'hates', 'racist', 'racism',
  // violence, crime and death
  'kill', 'killed', 'killer', 'killing', 'kills', 'murder', 'murdered', 'murderer', 'suicide', 'suicidal', 'shoot', 'shot', 'shooting', 'gun', 'guns', 'bomb', 'bombs', 'bombing',
  'terror', 'terrorist', 'terrorists', 'terrorism', 'war', 'wars', 'weapon', 'weapons', 'knife', 'attack', 'attacked', 'attacks', 'torture', 'massacre', 'genocide', 'corpse', 'blood',
  'bloody', 'stab', 'stabbed', 'hang', 'hanged', 'execute', 'executed', 'execution', 'die', 'died', 'dies', 'dying', 'dead', 'death', 'deaths', 'grave', 'funeral', 'coffin', 'cemetery',
  'victim', 'victims', 'crime', 'criminal', 'criminals', 'prison', 'jail', 'arrest', 'arrested', 'steal', 'stole', 'stolen', 'thief', 'robber', 'robbery', 'rob', 'kidnap', 'kidnapped',
  'hostage', 'violence', 'violent', 'abuse', 'abused', 'assault', 'fight', 'fought', 'beat', 'beaten', 'hit', 'hurt', 'injured', 'injury', 'wound', 'wounded', 'cancer', 'disease', 'virus',
  'poison', 'poisoned', 'bury', 'buried', 'sick', 'illness', 'tragedy', 'accident', 'crash', 'disaster', 'earthquake', 'tsunami', 'flood', 'fire',
  // drugs and drink
  'drug', 'drugs', 'cocaine', 'heroin', 'marijuana', 'cannabis', 'opium', 'weed', 'drunk', 'drunken', 'alcoholic', 'alcoholics', 'addict', 'addicted', 'addiction', 'cigarette', 'cigarettes',
  'smoke', 'smoking', 'smoked', 'tobacco', 'gambling', 'gamble', 'casino', 'bet',
  // politics, government and contested places
  'president', 'prime', 'minister', 'government', 'governments', 'politics', 'political', 'politician', 'politicians', 'election', 'elections', 'vote', 'votes', 'voted', 'voting', 'parliament',
  'congress', 'senate', 'senator', 'democrat', 'democratic', 'democracy', 'republican', 'republic', 'communist', 'communism', 'capitalism', 'capitalist', 'socialism', 'socialist', 'fascist',
  'nazi', 'nazis', 'hitler', 'trump', 'obama', 'putin', 'biden', 'stalin', 'mao', 'military', 'army', 'soldier', 'soldiers', 'navy', 'troops', 'immigrant', 'immigrants', 'immigration',
  'refugee', 'refugees', 'protest', 'protesters', 'revolution', 'dictator', 'dictatorship', 'regime', 'sanction', 'sanctions', 'nuclear', 'independence', 'taiwan', 'tibet', 'palestine',
  'palestinian', 'israel', 'israeli', 'ukraine', 'ukrainian', 'russia', 'russian', 'crimea', 'kashmir', 'kosovo', 'korea', 'korean', 'iraq', 'iran', 'afghanistan', 'syria', 'vietnam war',
  'hoang sa', 'truong sa', 'paracel', 'spratly', 'south china sea', 'east sea', 'cancel culture', 'woke', 'feminis', 'feminist', 'reincarnated', 'reincarnation', 'pants off', 'undress', 'undressed', 'strip', 'bikini', 'lingerie',
  'colony', 'colonial', 'empire', 'emperor', 'king', 'queen', 'tax', 'taxes', 'law', 'laws', 'court', 'judge', 'lawyer', 'police', 'policeman', 'officer', 'cop',
  // religion
  'god', 'gods', 'goddess', 'allah', 'jesus', 'christ', 'christian', 'christians', 'christianity', 'muslim', 'muslims', 'islam', 'islamic', 'jew', 'jews', 'jewish', 'judaism', 'buddhism',
  'buddhist', 'buddha', 'hindu', 'hinduism', 'bible', 'quran', 'koran', 'church', 'churches', 'mosque', 'synagogue', 'temple', 'pray', 'prayed', 'praying', 'prayer', 'prayers', 'bless',
  'blessed', 'blessing', 'heaven', 'devil', 'satan', 'demon', 'demons', 'angel', 'angels', 'sin', 'sins', 'sinner', 'soul', 'souls', 'holy', 'sacred', 'priest', 'pope', 'monk', 'nun',
  'religion', 'religions', 'religious', 'atheist', 'atheism', 'cult', 'ghost', 'ghosts', 'witch', 'curse', 'cursed', 'hell', 'lord', 'amen', 'worship', 'faith', 'spiritual', 'bishop',
  // personal and contact data, money pitches
  'email', 'e-mail', 'facebook', 'twitter', 'instagram', 'youtube', 'google', 'password', 'username', 'url', 'website', 'phone', 'telephone', 'mobile', 'address', 'passport', 'credit',
  'loan', 'bitcoin', 'lottery', 'rich', 'poor', 'poverty', 'money', 'tom\'s', 'mary\'s',
];
const VIETNAMESE_BLOCK = [
  'địt', 'đụ', 'đéo', 'lồn', 'cặc', 'buồi', 'đĩ', 'điếm', 'cave', 'mẹ kiếp', 'chết tiệt', 'khốn nạn', 'khốn kiếp', 'đồ ngu', 'ngu', 'ngu ngốc', 'đần', 'khùng', 'điên', 'ghét', 'tình dục', 'khỏa thân',
  'cưỡng hiếp', 'hiếp dâm', 'gái điếm', 'dâm', 'sex', 'chính trị', 'chính phủ', 'tổng thống', 'thủ tướng', 'bộ trưởng', 'bầu cử', 'quốc hội', 'cộng sản', 'quân đội', 'chiến tranh', 'khủng bố',
  'giết', 'chết', 'tự tử', 'tử vong', 'máu', 'ma túy', 'thuốc phiện', 'say rượu', 'nghiện', 'chúa', 'thượng đế', 'phật', 'hồi giáo', 'thiên chúa', 'cơ đốc', 'do thái', 'kinh thánh', 'nhà thờ',
  'cầu nguyện', 'thiên đường', 'địa ngục', 'ma quỷ', 'quỷ', 'tôn giáo', 'vô thần', 'đạo', 'linh hồn', 'thánh', 'đền', 'chùa', 'nhà tù', 'tù', 'cảnh sát', 'bắn', 'súng', 'bom', 'vũ khí',
  'mày', 'tao', 'thằng', 'đồ con', 'ung thư', 'bệnh', 'tai nạn', 'hoàng đế', 'vua', 'nữ hoàng', 'thuế', 'luật', 'tòa án', 'thẩm phán', 'luật sư', 'di dân', 'tị nạn', 'biểu tình', 'cách mạng', 'độc tài', 'hạt nhân',
];

// ---------------------------------------------------------------- helpers
const read = (name) => readFileSync(join(dir, name), 'utf8').split(/\r?\n/).filter(Boolean);
const counts = [];
const step = (name, rule, n) => { counts.push({ name, rule, n }); console.log(`${String(n).padStart(6)}  ${name}`); };
const typography = [[/[‘’‚‛′´`]/g, "'"], [/[“”„‟″«»]/g, '"'], [/[–—−]/g, '-'], [/…/g, '...'], [/[   ​]/g, ' ']];
const asciiEnglish = (text) => { let t = text; for (const [re, to] of typography) t = t.replace(re, to); return t.replace(/\s+/g, ' ').trim(); };
const cleanVietnamese = (text) => { let t = text; for (const [re, to] of typography) t = t.replace(re, to); return t.normalize('NFC').replace(/\s+/g, ' ').trim(); };
const words = (text) => text.toLowerCase().match(/[a-z]+(?:'[a-z]+)?|\d+/g) ?? [];
const key = (text) => text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/[^a-z0-9]+/g, ' ').trim();
const blockRe = (list) => new RegExp(`(?<![\\p{L}\\p{N}])(?:${list.map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})(?![\\p{L}\\p{N}])`, 'iu');
// words blocked by their stem (the word and everything that starts with it): circumcision, peed, urinate, genitals ...
const ENGLISH_STEMS = ['circumcis', 'peed', 'peeing', 'urin', 'defecat', 'genital', 'anus', 'rectum', 'ejaculat', 'erection', 'suck', 'piss', 'butt', 'slaughter', 'massacr', 'abus', 'molest', 'harass', 'exploit', 'racis', 'sexis', 'jihad', 'extremis', 'fanatic', 'communis', 'terroris', 'assassin', 'execut', 'genocid', 'smok', 'alcohol', 'beer', 'whisk', 'vodka', 'liquor', 'booze', 'wine', 'hangover', 'drunk', 'overdos', 'suicid', 'cancer', 'tumor', 'diseas', 'infect', 'epidemic', 'pandemic', 'covid', 'coronavirus', 'aids', 'hiv', 'syphilis', 'disabled', 'handicap', 'cripple', 'blind', 'deaf', 'mental', 'psycho', 'lunatic', 'bankrupt', 'debt', 'fraud', 'scam', 'cheat', 'liar', 'divorc', 'widow', 'orphan', 'mourn', 'tears', 'cry', 'cried', 'crying'];
const englishBlock = blockRe(ENGLISH_BLOCK);
const englishStem = new RegExp('(?<![\\p{L}])(?:' + ENGLISH_STEMS.join('|') + ')', 'iu');
const vietnameseBlock = blockRe(VIETNAMESE_BLOCK);
const vietnameseSpecific = /[ăâđêôơưàáảãạằắẳẵặầấẩẫậèéẻẽẹềếểễệìíỉĩịòóỏõọồốổỗộờớởỡợùúủũụừứửữựỳýỷỹỵ]/i;
const toneMarks = /[àáảãạằắẳẵặầấẩẫậèéẻẽẹềếểễệìíỉĩịòóỏõọồốổỗộờớởỡợùúủũụừứửữựỳýỷỹỵ]/i;

// ---------------------------------------------------------------- load
const split = (line) => line.split('\t');
const sentences = (name, lang) => { const map = new Map(); for (const line of read(name)) { const [id, l, text, user] = split(line); if (l === lang) map.set(id, { id: Number(id), text, user: user && user !== '\\N' ? user : '' }); } return map; };
const tags = (name) => { const map = new Map(); for (const line of read(name)) { const [id, tag] = split(line); (map.get(id) ?? map.set(id, []).get(id)).push(tag); } return map; };
const engById = sentences('eng_sentences_detailed.tsv', 'eng');
const vieById = sentences('vie_sentences_detailed.tsv', 'vie');
// how often each word and each Vietnamese syllable occurs in ALL the Tatoeba sentences of the language: a word that occurs almost nowhere else is a typo
const frequency = (map, split) => { const f = new Map(); for (const s of map.values()) for (const w of split(s.text)) f.set(w, (f.get(w) ?? 0) + 1); return f; };
const englishFrequency = frequency(engById, (t) => t.toLowerCase().match(/[a-z]{3,}/g) ?? []);
const vietnameseFrequency = frequency(vieById, (t) => t.normalize('NFC').toLowerCase().match(/\p{L}+/gu) ?? []);
const engTags = tags('eng_tags.tsv');
const vieTags = tags('vie_tags.tsv');
const links = read('vie-eng_links.tsv').map((line) => { const [v, e] = split(line); return { v, e }; });

// the existing sentences, to avoid duplicates
const rawText = readFileSync(quotesPath, 'utf8');
const eol = rawText.includes('\r\n') ? '\r\n' : '\n';
const fileText = rawText.replace(/\r\n/g, '\n'); // worked on with LF; the file's own line endings are put back when it is written
const tatoebaStart = fileText.search(/\n {4}\{\n {6}"category": "conversation",\n {6}"source": "Tatoeba/);
const keptText = tatoebaStart >= 0 ? fileText.slice(0, tatoebaStart) + '\n  ]\n}\n' : fileText;
const kept = JSON.parse(keptText);
const existingEnglish = new Set(), existingVietnamese = new Set();
let existingCount = 0;
for (const g of kept.groups) for (const q of g.quotes) { existingEnglish.add(key(q.english)); existingVietnamese.add(key(q.vietnamese)); existingCount++; }

// ---------------------------------------------------------------- the steps
step('Links between a Vietnamese and an English sentence in the export', 'vie-eng_links.tsv', links.length);
let pairs = links.filter((l) => engById.has(l.e) && vieById.has(l.v)).map((l) => ({ e: engById.get(l.e), v: vieById.get(l.v) }));
step('Both sentences are in the exports with the right language', 'a link whose sentence is missing or in another language is dropped', pairs.length);

pairs = pairs.filter((p) => p.e.user && p.v.user);
step('Both sentences have a known author', 'CC BY needs the author\'s name: a sentence without an author (orphaned) cannot be credited', pairs.length);

pairs = pairs.map((p) => ({ ...p, en: asciiEnglish(p.e.text), vi: cleanVietnamese(p.v.text) }));
pairs = pairs.filter((p) => /^[\x20-\x7E]+$/.test(p.en) && !/[<>@#\\{}\[\]|_*~^=]|https?:|www\./i.test(p.en + p.vi) && !/[\u0000-\u001F]/.test(p.vi));
step('Text cleanup: plain ASCII English and no markup, links or odd characters', 'typographic quotes and dashes become ASCII; whitespace collapsed; Vietnamese NFC; English with any other character, or either text with markup, a link or @ # < > [ ] { } \\ | _ * ~ ^ =, is dropped', pairs.length);

pairs = pairs.filter((p) => {
  const w = words(p.en);
  const digits = (p.en.match(/\d/g) ?? []).length;
  const openQuote = (p.en.match(/"/g) ?? []).length % 2 === 0 && (p.vi.match(/"/g) ?? []).length % 2 === 0;
  return p.en.length >= RULES.minEnglishChars && p.en.length <= RULES.maxEnglishChars && w.length >= RULES.minEnglishWords && w.length <= RULES.maxEnglishWords
    && p.vi.length <= RULES.maxVietnameseChars && p.vi.length >= 6 && digits <= RULES.maxDigitsPerSentence && openQuote
    && /^["']?[A-Z]/.test(p.en) && /^["']?\p{Lu}/u.test(p.vi) && /[.?!]["']?$/.test(p.en) && /[.?!]["']?$/.test(p.vi) && !/\.\.|[!?]{2,}|\s[,.;:!?]/.test(p.en + p.vi) && !/\b[A-Z]{4,}\b/.test(p.en);
});
step(`Shape and length: ${RULES.minEnglishWords}-${RULES.maxEnglishWords} words, ${RULES.minEnglishChars}-${RULES.maxEnglishChars} English and at most ${RULES.maxVietnameseChars} Vietnamese characters, a capital start and a full stop, ? or ! at the end`, 'so that every sentence fits the widget in at most three lines at its smallest size; no "...", no repeated punctuation, no shouting, balanced quotes, at most 6 digits', pairs.length);

pairs = pairs.filter((p) => !(engTags.get(String(p.e.id)) ?? []).some((t) => EXCLUDED_TAG.test(t)) && !(vieTags.get(String(p.v.id)) ?? []).some((t) => EXCLUDED_TAG.test(t)));
step('Tatoeba tags: nothing tagged vulgar, slang, ambiguous, non-literal, "change" or "check"', 'the community\'s own flags on the English or the Vietnamese sentence', pairs.length);

pairs = pairs.filter((p) => !englishBlock.test(p.en) && !englishStem.test(p.en) && !vietnameseBlock.test(p.vi.toLowerCase()));
step('Content: no adult, offensive, violent, drug, alcohol, illness, political, contested-place or religious content, and no personal data', `an English word list (${ENGLISH_BLOCK.length} words and ${ENGLISH_STEMS.length} word stems) and a Vietnamese one (${VIETNAMESE_BLOCK.length}); deliberately wide, so some harmless sentences are lost`, pairs.length);

// names that are kept as they are in the translation: a capitalised word in mid-sentence that most pairs keep in the Vietnamese too
const capWords = (p) => (p.en.match(/(?<=[a-z,;'] )[A-Z][a-z]+/g) ?? []).filter((w) => w !== 'I');
const firstWord = (p) => (p.en.match(/^["']?([A-Z][a-z]+)/) ?? [])[1];
const seen = new Map(), kept2 = new Map();
for (const p of pairs) for (const w of new Set(capWords(p))) { seen.set(w, (seen.get(w) ?? 0) + 1); if (p.vi.toLowerCase().includes(w.toLowerCase())) kept2.set(w, (kept2.get(w) ?? 0) + 1); }
const preserved = new Set([...seen].filter(([w, n]) => n >= 4 && (kept2.get(w) ?? 0) / n >= 0.8).map(([w]) => w));
for (const w of ['Tom', 'Mary', 'John', 'Ken', 'Bob', 'Jane', 'Mike', 'Ann', 'Anne', 'Taro', 'Jack', 'Bill', 'Paul', 'Lucy', 'Nancy', 'Betty', 'Tim', 'Sam']) preserved.add(w);

function faithful(p) {
  const en = p.en, vi = p.vi;
  const ratio = vi.length / en.length;
  if (ratio < RULES.lengthRatio[0] || ratio > RULES.lengthRatio[1]) return false;
  const viWords = vi.split(' ').filter(Boolean).length;
  const enWords = words(en).length;
  if (viWords / enWords < RULES.wordRatio[0] || viWords / enWords > RULES.wordRatio[1]) return false;
  // a Vietnamese sentence of any length has tone marks or the special letters: text without diacritics is mis-typed or not Vietnamese
  if (!vietnameseSpecific.test(vi) && viWords > 2) return false;
  if (viWords >= 4 && !toneMarks.test(vi)) return false;
  // the same question, exclamation or statement
  const e = en.replace(/["']+$/, '').slice(-1), v = vi.replace(/["']+$/, '').slice(-1);
  if ((e === '?') !== (v === '?')) return false;
  if (e === '.' && v === '!' && !/^(Please|Let|Don't|Do not|Be|Go|Come|Stop|Look|Wait)\b/i.test(en)) { /* a full stop may become ! in an imperative; otherwise the tone differs */ return false; }
  // the same numbers
  const nums = (t) => (t.match(/\d+/g) ?? []).sort().join(',');
  if (nums(en) !== nums(vi)) return false;
  // names that translations keep must be there
  // (a name at the start of the sentence cannot be told from any other capitalised word, so only the names that translations keep are checked there too)
  for (const w of new Set([...capWords(p), ...(firstWord(p) && preserved.has(firstWord(p)) ? [firstWord(p)] : [])])) if (preserved.has(w) && !vi.toLowerCase().includes(w.toLowerCase())) return false;
  // not a copy: some English words left in the Vietnamese means an untranslated or mixed sentence
  const copied = words(vi).filter((w) => w.length >= 5 && /^[a-z]+$/.test(w) && en.toLowerCase().includes(w) && !preserved.has(w[0].toUpperCase() + w.slice(1)));
  if (copied.length >= 1) return false;
  // spelling: no English word that occurs in at most two of the 2 million English sentences, and no Vietnamese syllable that occurs only here
  if ((en.toLowerCase().match(/[a-z]{3,}/g) ?? []).some((w) => (englishFrequency.get(w) ?? 0) <= 2)) return false;
  const capitalised = new Set((vi.match(/\p{Lu}\p{L}*/gu) ?? []).map((w) => w.toLowerCase()));
  if ((vi.normalize('NFC').toLowerCase().match(/\p{L}+/gu) ?? []).some((w) => !capitalised.has(w) && (vietnameseFrequency.get(w) ?? 0) <= 1)) return false;
  return true;
}
pairs = pairs.filter(faithful);
step('Faithful translation checks (heuristic)', 'Vietnamese/English length and word-count ratios in range; same ?, ! or . ending; the same numbers; the names that translations keep are present; Vietnamese has its tone marks and no copied English words; spelling: no English word that occurs in at most two of the 2 million English sentences of Tatoeba, and no Vietnamese syllable that occurs in no other Vietnamese sentence', pairs.length);

// one pair for each English sentence and one for each Vietnamese sentence, the best first: a "verified" Vietnamese tag, then a "verified" English tag, then a length ratio near the usual 1.2
const score = (p) => (((vieTags.get(String(p.v.id)) ?? []).includes('OK') ? 4 : 0) + ((engTags.get(String(p.e.id)) ?? []).includes('OK') ? 2 : 0)) * 10 - Math.abs(p.vi.length / p.en.length - 1.2);
pairs.sort((a, b) => score(b) - score(a) || a.v.id - b.v.id);
const byEnglish = new Set(), byVietnamese = new Set(), unique = [];
for (const p of pairs) { if (byEnglish.has(p.e.id) || byVietnamese.has(p.v.id)) continue; byEnglish.add(p.e.id); byVietnamese.add(p.v.id); unique.push(p); }
pairs = unique;
step('One pair per sentence', 'when a sentence has several translations (or a translation several sources) the best is kept: verified tags first, then the usual length ratio', pairs.length);

const taken = new Set(), takenVi = new Set(), final = [];
let dupExisting = 0, dupOwn = 0;
for (const p of pairs) {
  const k = key(p.en), kv = key(p.vi);
  if (existingEnglish.has(k) || existingVietnamese.has(kv)) { dupExisting++; continue; }
  if (taken.has(k) || takenVi.has(kv)) { dupOwn++; continue; }
  taken.add(k); takenVi.add(kv); final.push(p);
}
step('Not a duplicate of an existing sentence or of each other (ignoring case, accents and punctuation)', `${dupExisting} equal to one of the ${existingCount} existing sentences, ${dupOwn} equal to a sentence kept before them`, final.length);
final.sort((a, b) => a.e.id - b.e.id);

// ---------------------------------------------------------------- write
const q = (s) => JSON.stringify(s).replace(/[\u007F-￿]/g, (c) => c); // keep real characters, escape nothing else
const entry = (p) => `        { "id": ${p.e.id}, "author": ${q(p.e.user)}, "english": ${q(p.en)}, "vietnamese": ${q(p.vi)}, "vid": ${p.v.id}, "vauthor": ${q(p.v.user)} }`;
const group = `    {
      "category": "conversation",
      "source": "Tatoeba (https://tatoeba.org), exports of ${exportsDate}: English sentences with Vietnamese translations. Each entry gives the Tatoeba sentence id (id) and author (author) of the English sentence and the id (vid) and author (vauthor) of its Vietnamese translation; see https://tatoeba.org/en/sentences/show/<id>",
      "license": "English and Vietnamese: CC BY 2.0 FR (https://creativecommons.org/licenses/by/2.0/fr/): attribution to the author of each sentence, as given in the entry",
      "quotes": [
${final.map(entry).join(',\n')}
      ]
    }`;
const bodyEnd = keptText.lastIndexOf('\n  ]\n}');
const output = keptText.slice(0, bodyEnd).replace(/\s*$/, '') + ',\n' + group + '\n  ]\n}\n';
JSON.parse(output); // must stay valid
writeFileSync(quotesPath, eol === '\n' ? output : output.replace(/\n/g, eol), 'utf8');

const report = `# Tatoeba import report

Generated by \`tools/import-tatoeba.mjs\` from the Tatoeba exports of ${exportsDate} (https://downloads.tatoeba.org/exports/per_language/). Pairs left after each step:

| Step | Pairs left | Rule |
| --- | ---: | --- |
${counts.map((c) => `| ${c.name} | ${c.n} | ${c.rule} |`).join('\n')}

Final: **${final.length}** pairs added to the ${existingCount} existing sentences (category \`conversation\`).
`;
writeFileSync(reportPath, report, 'utf8');
console.log(`\nWrote ${final.length} pairs to ${quotesPath}\nReport: ${reportPath}`);
