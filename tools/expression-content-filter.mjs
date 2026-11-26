/**
 * The content rules of the expression importers: offensive, vulgar and adult material is left out. Idioms and proverbs that mention death,
 * drink or religion are ordinary English and stay (owner decision), so this list is narrower than the one of the Tatoeba sentence import.
 */

// whole words
const WORDS = [
  // adult and sexual
  'sex', 'sexy', 'sexed', 'porn', 'porno', 'naked', 'nude', 'nudity', 'rape', 'raped', 'rapes', 'raping', 'rapist', 'whore', 'whores', 'slut', 'sluts', 'hooker', 'hookers',
  'stripper', 'boob', 'boobs', 'tit', 'tits', 'titties', 'nipple', 'nipples', 'horny', 'cock', 'cocks', 'dick', 'dicks', 'prick', 'pussy', 'cum', 'semen', 'sperm', 'condom', 'condoms',
  'anal', 'anus', 'arse', 'arsehole', 'ass', 'asses', 'asshole', 'butthole', 'scrotum', 'testicle', 'testicles', 'bollocks', 'clitoris', 'vulva', 'vagina', 'penis', 'phallus',
  'foreskin', 'pubic', 'groin', 'crotch', 'breasts', 'panties', 'lingerie', 'virgin', 'virginity', 'brothel', 'pimp', 'pimps', 'fetish', 'kinky', 'orgy', 'lust', 'lewd', 'obscene',
  'wank', 'wanker', 'wanking', 'shag', 'shagging', 'bugger', 'buggery', 'sodomy', 'sodomise', 'sodomize', 'hump', 'humping', 'boner', 'blowjob', 'handjob', 'smut',
  // vulgar
  'fuck', 'fucks', 'fucked', 'fucker', 'fucking', 'motherfucker', 'shit', 'shits', 'shitty', 'shat', 'bullshit', 'piss', 'pissed', 'pisses', 'pissing', 'crap', 'crappy', 'turd', 'turds',
  'fart', 'farts', 'farted', 'farting', 'poop', 'bitch', 'bitches', 'bastard', 'bastards', 'cunt', 'cunts', 'twat', 'goddamn', 'goddamned', 'douche', 'douchebag', 'jackass', 'dumbass',
  // slurs and insults of a group
  'nigger', 'niggers', 'nigga', 'negro', 'faggot', 'faggots', 'fag', 'dyke', 'tranny', 'retard', 'retards', 'retarded', 'spastic', 'chink', 'gook', 'kike', 'spic', 'wetback', 'raghead',
  'paki', 'golliwog', 'half-breed', 'mongoloid', 'cripple', 'midget', 'gypsy', 'gyp', 'gypped',
];
// words that start with these
const STEMS = [
  'sexual', 'sexuall', 'porno', 'masturbat', 'ejaculat', 'orgasm', 'erotic', 'erection', 'genital', 'intercourse', 'copulat', 'fellat', 'cunnilingus', 'incest', 'paedophil', 'pedophil',
  'prostitut', 'fornicat', 'molest', 'defecat', 'urinat', 'excrement', 'faeces', 'feces', 'aroused sexually', 'seduc', 'promiscu', 'libido', 'aphrodisiac', 'menstruat', 'contracepti',
  'motherfuck', 'cocksuck', 'shithead', 'slutt', 'whorehouse', 'ethnic slur', 'derogatory term', 'offensive term', 'vulgar',
];
const VIETNAMESE = [
  'địt', 'đụ', 'đéo', 'lồn', 'cặc', 'buồi', 'đĩ', 'điếm', 'gái mại dâm', 'mại dâm', 'tình dục', 'giao hợp', 'giao cấu', 'khỏa thân', 'khoả thân', 'cưỡng hiếp', 'hiếp dâm', 'dâm',
  'dương vật', 'âm đạo', 'âm hộ', 'thủ dâm', 'cứt', 'đồ chó đẻ', 'chó đẻ', 'mẹ kiếp', 'đồ khốn', 'khốn nạn', 'vãi',
];

const escape = (w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const whole = (list) => new RegExp(`(?<![\\p{L}\\p{N}])(?:${list.map(escape).join('|')})(?![\\p{L}\\p{N}])`, 'iu');
const englishWords = whole(WORDS);
const englishStems = new RegExp(`(?<![\\p{L}])(?:${STEMS.map(escape).join('|')})`, 'iu');
const vietnameseWords = whole(VIETNAMESE);

/** True when the English text mentions something this app leaves out. */
export const blockedEnglish = (text) => englishWords.test(text) || englishStems.test(text);
/** True when the Vietnamese text mentions something this app leaves out. */
export const blockedVietnamese = (text) => vietnameseWords.test(text.normalize('NFC').toLowerCase());
/** Sense tags of Wiktionary that mark a meaning as one to leave out. */
export const OFFENSIVE_TAGS = new Set(['vulgar', 'offensive', 'slur', 'derogatory', 'ethnic', 'pejorative']);
/** Wiktionary category names that mark an adult or offensive meaning. */
export const BLOCKED_CATEGORY = /\bsex\b|sexuality|masturbation|prostitution|pornograph|genitalia|bodily fluids|slurs|vulgarities|swear words|offensive/i;
export const LIST_SIZES = { words: WORDS.length, stems: STEMS.length, vietnamese: VIETNAMESE.length };
