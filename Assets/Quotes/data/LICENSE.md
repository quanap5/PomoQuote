# Licence of the data files in this folder

PomoQuote's **code** is MIT licensed. The **data files in this folder are not**: each is under the licence given here. Every entry also names its own licence and source.

| Files | Source | Licence |
| --- | --- | --- |
| `wiktionary-idioms.jsonl`, `wiktionary-phrasal-verbs.jsonl`, `wiktionary-expressions.jsonl`, `wiktionary-proverbs.jsonl` | [English Wiktionary](https://en.wiktionary.org), through the [kaikki.org](https://kaikki.org/dictionary/English/) extract, retrieved 2026-11-26 | [Creative Commons Attribution-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-sa/4.0/) ([legal code](https://creativecommons.org/licenses/by-sa/4.0/legalcode)) |
| `public-domain-quotes.jsonl`, `public-domain-maxims.jsonl` | Sentences from books by authors (and translators) who died in 1955 or before, published before 1930 | English: public domain. Vietnamese made for PomoQuote: [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |

## Public-domain files

- **English:** each sentence stands word for word in the work its entry names (`attribution.work`); the author and, for a translated work, the translator are given with the year of their death. The books and what was read of each are listed in [`tools/public-domain-works.mjs`](../../../tools/public-domain-works.mjs). "Public domain" rests on the authors' death dates (checked against Wikidata) and publication before 1930; the law of your country may differ.
- **Vietnamese:** drafted by a language model for PomoQuote, not reviewed by a person, and dedicated to the public domain under CC0 1.0.

## English Wiktionary files (CC BY-SA 4.0)

- **Authors:** the contributors of English Wiktionary. Each entry gives the address of its page (`source.url`); that page's history lists the authors.
- **Changes made:** entries were filtered and selected; one meaning was chosen for each and, where there is one, one short usage example written for the dictionary (no quotation from another work); typography was reduced to plain characters; a Vietnamese translation of the headword, and of most examples, was added.
- **Translations:** the Vietnamese added for PomoQuote is also offered under CC BY-SA 4.0. Where `translations[].status` is `machine_unreviewed` it was drafted by a language model and no person has reviewed it; where it is `source` it is the dictionary's own translation.
- **Your rights:** you may copy, share and adapt these files, also commercially, if you credit the authors, link the licence, say what you changed, and share your adaptations under the same licence.
- The material is provided without warranty. Neither Wiktionary nor the Wikimedia Foundation endorses PomoQuote. Wiktionary's texts are also offered under the GNU Free Documentation License; PomoQuote uses them under CC BY-SA 4.0.
