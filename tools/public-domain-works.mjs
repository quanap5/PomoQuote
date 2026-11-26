/**
 * The books the quotes and life maxims are taken from. Every author (and translator) died in 1955 or before, and every book was published
 * before 1930, so the words are in the public domain. A sentence is only used when it stands, word for word, in the author's own text: the
 * line range of each file leaves out editors' introductions, prefaces by others, notes and indexes.
 *
 * Death dates were checked against Wikidata on 2026-11-26 (the id of each author's page is recorded here and in every entry); Casaubon (d. 1671),
 * J. W. Willis-Bund (d. 1928), J. H. Friswell (d. 1878), George Long (d. 1879) and James Legge (d. 1897) likewise. W. H. D. Rouse, who edited
 * the Meditations edition used, died in 1950; only the body of Casaubon's translation is read.
 *
 * In a book marked `fiction` the quoted speech is the author's own writing; in every other prose book whatever stands in quotation marks or in
 * an indented block is left out, because it may be somebody else's words.
 *
 * `file` is the ebook number in the plain-text archive it was downloaded from (https://gutenberg.pglaf.org/cache/epub/<n>/pg<n>.txt), `sha256`
 * pins the exact file the line ranges belong to. The shipped data carries only the public-domain words, not the archive's name or files.
 */
export const WORKS = [
  // ------------------------------------------------------------ life maxims: books of counsel and aphorisms
  { file: 9105, sha256: '1bbcb97795d8d482f60596e1e1fca00bb6a2dfa30e90e8d86aec16f0e5da81bc', from: 889, to: 3879, mode: 'numbered', type: 'maxim', author: 'Francois de La Rochefoucauld', died: 1680, wikidata: 'Q273708', translator: 'J. W. Willis-Bund and J. H. Friswell', translatorDied: 1928, work: 'Reflections; or Sentences and Moral Maxims', year: 1871 },
  { file: 102, sha256: '449499a3e5c0947b0172aa4e445e7c98d9ff33d7892e98cd5157a4f012414c5f', from: 137, to: 5753, mode: 'calendar', type: 'maxim', author: 'Mark Twain', died: 1910, wikidata: 'Q7245', work: "Pudd'nhead Wilson", year: 1894 },
  { file: 2895, sha256: '42f57a851bd6c950b47e7c1ab2a8d3746055315a76229f4a9ab800210f386c2d', from: 615, to: 19149, mode: 'calendar', type: 'maxim', author: 'Mark Twain', died: 1910, wikidata: 'Q7245', work: 'Following the Equator', year: 1897 },
  { file: 4094, sha256: 'e92780d776ce096aac09e8416a70e1d421d3074de9e35c852c0565b456e42d30', from: 91, to: 4525, mode: 'master', type: 'maxim', author: 'Confucius', died: -479, wikidata: 'Q4604', translator: 'James Legge', translatorDied: 1897, work: 'The Analects', year: 1861 },
  { file: 216, sha256: '041568f8dfef3ac6c2deb7699756f9d26d6aa8669647f4ce611b0ed4a95b0db6', from: 57, to: 1496, mode: 'prose', type: 'maxim', author: 'Lao Tzu', died: -500, wikidata: 'Q9333', translator: 'James Legge', translatorDied: 1897, work: 'The Tao Te Ching', year: 1891 },
  { file: 10661, sha256: '7915eac7442daed1fbc461b8a281ecc315c5e619126d50d914c29c39fa3b2209', from: 151, to: 5373, mode: 'prose', type: 'maxim', author: 'Epictetus', died: 138, wikidata: 'Q183144', translator: 'George Long', translatorDied: 1879, work: 'Discourses and Encheiridion', year: 1877 },
  { file: 2680, sha256: '6977474f82a39dd88a4af31a59504821658174d33c177732c54357e4d680b96c', from: 579, to: 5736, mode: 'prose', type: 'maxim', author: 'Marcus Aurelius', died: 180, wikidata: 'Q1430', translator: 'Meric Casaubon', translatorDied: 1671, work: 'Meditations', year: 1634 },
  { file: 575, sha256: '4434aa015d325e65816eb0e694e7c573fa4b71523530e45adfeb23e7fcf63bf1', from: 148, to: 5187, mode: 'prose', type: 'maxim', author: 'Francis Bacon', died: 1626, wikidata: 'Q37388', work: 'Essays', year: 1625 },
  { file: 3361, sha256: '2f9ed99eff7d288310f615b2f1210e50a7d8ccfaa339d3e5d054d3997b4df84d', from: 199, to: 27700, mode: 'prose', type: 'maxim', author: 'Lord Chesterfield', died: 1773, wikidata: 'Q332487', work: 'Letters to His Son', year: 1774 },
  { file: 935, sha256: 'e7f4232f8880af072453521a95d72b3476d716e2485de08ab8e54c444f28802a', from: 602, to: 12728, mode: 'prose', type: 'maxim', author: 'Samuel Smiles', died: 1904, wikidata: 'Q1195422', work: 'Self-Help', year: 1859 },
  { file: 4507, sha256: '5c189ac57a410e844895e9e054630cab10138d145952a72ccc200702076e2aa9', from: 128, to: 987, mode: 'prose', type: 'maxim', author: 'James Allen', died: 1912, wikidata: 'Q1371577', work: 'As a Man Thinketh', year: 1903 },
  { file: 7952, sha256: '739db12475300d600fc9d3525e7766824fc9e482cf7086d6d2f3abf82f816977', from: 263, to: 8337, mode: 'prose', type: 'maxim', author: 'John Lubbock', died: 1913, wikidata: 'Q312503', work: 'The Pleasures of Life', year: 1887 },
  { file: 21291, sha256: '03b64b75af5dfdddb7924a8a74b13c9e6821aae68ec84be8daf423cb8e58e663', from: 294, to: 27297, mode: 'prose', type: 'maxim', author: 'Orison Swett Marden', died: 1924, wikidata: 'Q7102841', work: 'Pushing to the Front', year: 1911 },
  // ------------------------------------------------------------ quotes: essays, memoirs, novels and plays
  { file: 2944, sha256: '5d9d28c12da8bd9aaf3043e9a2d08fbd46116efd5a3736490a393599aef6d278', from: 76, to: 6891, mode: 'prose', type: 'quote', author: 'Ralph Waldo Emerson', died: 1882, wikidata: 'Q48226', work: 'Essays, First Series', year: 1841 },
  { file: 2945, sha256: '2e7eb7281ade2ab623b9e4a790f79ed8e8b25dc1d95d31f9eda4f7a7dc471e6e', from: 40, to: 5199, mode: 'prose', type: 'quote', author: 'Ralph Waldo Emerson', died: 1882, wikidata: 'Q48226', work: 'Essays, Second Series', year: 1844 },
  { file: 39827, sha256: '5a5199bd09e7792dadb84e4771d28c595c50803e0033924501f0b6a4d855c2ae', from: 79, to: 6347, mode: 'prose', type: 'quote', author: 'Ralph Waldo Emerson', died: 1882, wikidata: 'Q48226', work: 'The Conduct of Life', year: 1860 },
  { file: 205, sha256: '2d9a76a2e3e8195c69430516ebd33c4d0757a53ad432ff6186b7b794e6fe99f9', from: 81, to: 10248, mode: 'prose', type: 'quote', author: 'Henry David Thoreau', died: 1862, wikidata: 'Q131149', work: 'Walden and Civil Disobedience', year: 1854 },
  { file: 174, sha256: '38f36b510417177aa87a6a24c968e3ec63a447b7df36a9c1a7c5a3f2d9e51547', from: 60, to: 8554, mode: 'prose', fiction: true, type: 'quote', author: 'Oscar Wilde', died: 1900, wikidata: 'Q30875', work: 'The Picture of Dorian Gray', year: 1891 },
  { file: 790, sha256: '779c1aa1de9e8b00e04a30ee502758f24136e331919c8b13911068c02b37f648', from: 168, to: 2949, mode: 'play', type: 'quote', author: 'Oscar Wilde', died: 1900, wikidata: 'Q30875', work: "Lady Windermere's Fan", year: 1892 },
  { file: 854, sha256: 'de9fba7d70012b134103deea62870e3881ced03ab8ff97bb4cd93d1b14eeea10', from: 159, to: 3309, mode: 'play', type: 'quote', author: 'Oscar Wilde', died: 1900, wikidata: 'Q30875', work: 'A Woman of No Importance', year: 1893 },
  { file: 885, sha256: 'c59b8e04974f62a0bb67fd10dc0555932fc837bd57317ecb34801bd20d29943b', from: 145, to: 4502, mode: 'play', type: 'quote', author: 'Oscar Wilde', died: 1900, wikidata: 'Q30875', work: 'An Ideal Husband', year: 1895 },
  { file: 844, sha256: '1b8a58099bb1cdef6a845277a4bacf2f4a268702c165bde30124d4b5105d1851', from: 78, to: 3918, mode: 'play', type: 'quote', author: 'Oscar Wilde', died: 1900, wikidata: 'Q30875', work: 'The Importance of Being Earnest', year: 1895 },
  { file: 20203, sha256: 'c5be0e49f68f0876be480074b9dc0712a8fdbc7faa7def3acc6765afba9bd128', from: 529, to: 7234, mode: 'prose', type: 'quote', author: 'Benjamin Franklin', died: 1790, wikidata: 'Q34969', work: 'Autobiography', year: 1791 },
  { file: 1342, sha256: '3f6bb9d6f78e0293b56acd4714dd68cb7d6d1d293402031ce9d5a216bcaf9d75', from: 701, to: 14500, mode: 'prose', fiction: true, type: 'quote', author: 'Jane Austen', died: 1817, wikidata: 'Q36322', work: 'Pride and Prejudice', year: 1813 },
  { file: 145, sha256: 'dc2e0107e6ae07e8e33da934b2ec4e8e600826a2e0cd48df265119cd02e5fb50', from: 212, to: 33305, mode: 'prose', fiction: true, type: 'quote', author: 'George Eliot', died: 1880, wikidata: 'Q131333', work: 'Middlemarch', year: 1871 },
  { file: 652, sha256: 'b9dc2c43609243480ec20bcc1ffd2a199e5ff7c4c7bdaa9bcd6c2006e8b8a9bf', from: 168, to: 4113, mode: 'prose', fiction: true, type: 'quote', author: 'Samuel Johnson', died: 1784, wikidata: 'Q183266', work: 'Rasselas', year: 1759 },
  { file: 16287, sha256: '028b3ffd748162cfa99782479263668784e2a8f0805fb1f55175fe23a897d824', from: 275, to: 6124, mode: 'prose', type: 'quote', author: 'William James', died: 1910, wikidata: 'Q125249', work: 'Talks to Teachers', year: 1899 },
  { file: 2376, sha256: '084e7f54e60b9ae26ecf2ab23f1789db44d926c3b02106a984f475f9ccc7bdbe', from: 379, to: 7177, mode: 'prose', type: 'quote', author: 'Booker T. Washington', died: 1915, wikidata: 'Q319871', work: 'Up from Slavery', year: 1901 },
  { file: 23, sha256: '234c15348a66919bad1d534cdd48ee8ddf91f50115a706cbefaa8edd049672fd', from: 520, to: 3508, mode: 'prose', type: 'quote', author: 'Frederick Douglass', died: 1895, wikidata: 'Q215562', work: 'Narrative of the Life of Frederick Douglass', year: 1845 },
  { file: 3020, sha256: 'ed6589e2d22d6e19cd325179d0e96b31f2d8528be80399f356daea4b9fd01edb', from: 97, to: 14408, mode: 'prose', skip: [/^NOTES to ESSAY/i, /^ESSAY [IVXL]+\b/], type: 'quote', author: 'William Hazlitt', died: 1830, wikidata: 'Q126596', work: 'Table Talk', year: 1821 },
  { file: 386, sha256: '7e5ed94de96d304a5a49965d58095774252a88d1930d8cceaa63cd2ca1b7165b', from: 43, to: 4252, mode: 'prose', type: 'quote', author: 'Robert Louis Stevenson', died: 1894, wikidata: 'Q1512', work: 'Virginibus Puerisque', year: 1881 },
  { file: 130, sha256: 'ec525c93afddb05465bf6fa0af8f873078b8e42a2a24f9b999582994f054fdf2', from: 95, to: 5875, mode: 'prose', type: 'quote', author: 'G. K. Chesterton', died: 1936, wikidata: 'Q183167', work: 'Orthodoxy', year: 1908 },
  { file: 1293, sha256: 'b3c50ffec8f241eec071720a2f1c344d0b69de9ca02ec38bc36ffc1882ed453d', from: 642, to: 4147, mode: 'prose', type: 'quote', author: 'John Ruskin', died: 1900, wikidata: 'Q179126', work: 'Sesame and Lilies', year: 1865 },
];
