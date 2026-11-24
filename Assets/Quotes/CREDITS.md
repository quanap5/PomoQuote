# Sentence data credits

`quotes.json` holds the 357 English sentences with Vietnamese translations that PomoQuote shows. It is UTF-8 (no byte order mark). The data is organised in groups; each group has one **category** (`motivation`, `idiom`, `conversation`), one **source** and one **license**, and a sentence can add its own **author**. The app and its tests (`PomoQuote.Tests/QuoteDataTests.cs`) reject an entry with an empty field, an unknown category, a duplicate or a missing translation.

| Group | Sentences | English text | License |
|-------|-----------|--------------|---------|
| Public-domain quotations (motivation) | 44 | Sayings and writings of authors who died more than 100 years ago (Lao Tzu, Confucius, Seneca, Franklin, Emerson, Thoreau, Lincoln, Jefferson, Shakespeare, Samuel Johnson, Pasteur, Nietzsche, Tolstoy and others), wording as commonly published. A few are only *traditionally attributed* to the person; the author field says "(attributed)" there. | Public domain |
| Original motivation | 69 | Written for PomoQuote about focus, rest and learning English. | CC0 1.0 |
| Idioms and proverbs | 131 | Traditional English proverbs and idioms (anonymous folk sayings; three older lines credited to Pope and Shakespeare, both public domain). | Public domain |
| Conversation | 113 | Everyday sentences (greetings, work, food, travel, health, plans) written for PomoQuote. | CC0 1.0 |

**Vietnamese translations** were written for this project (not copied from any source and not word-for-word machine output): where Vietnamese has an equivalent proverb it is used (for example "Hoạn nạn mới biết bạn hiền"), otherwise the sentence is translated for meaning in everyday Vietnamese. They are dedicated to the public domain under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/). They have **not been reviewed by a native speaker**; corrections are welcome (edit `quotes.json`, run `dotnet test`).

**What was left out on purpose:** no sentence was taken from quote websites, and no modern copyrighted quotation is included. Short conversational pairs from [Tatoeba](https://tatoeba.org) (CC BY 2.0 FR) were considered but not used, because the conversation sentences are original; if Tatoeba sentences are added later, record each one's attribution in the file's `source` field.

The data is read through `IQuoteProvider`; only the bundled offline provider exists, and the app makes no network request for sentences.
