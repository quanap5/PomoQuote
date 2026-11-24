# Third-party assets

PomoQuote itself is MIT licensed (see [LICENSE](LICENSE)). The assets with their own terms are listed here. Other bundled assets (sentences, mascot, sounds, wallpapers, weather icons, PomoQuote logo) are listed in the `CREDITS.md` file of their folder.

## Provider logos (`Assets/Providers/`)

PomoQuote shows the logo of the AI service whose quota it displays, next to the numbers, so the user can tell the providers apart (nominative use). **The names and marks of OpenAI, Anthropic and Google belong to their owners. PomoQuote is not affiliated with, endorsed by or sponsored by any of them.** A file licence (CC0, MIT) is not a trademark licence, so each vendor's own terms were checked; the result per provider is below. Nothing is fetched at run time: the files are packaged in the program. The logos are only scaled (never recoloured, cropped or combined with other marks); an error, stale data or a near-full quota is shown by a separate badge and by text, never by changing the logo.

| File | Provider | What it is | Source (retrieved 2026-11-24) | Terms | Changes |
|------|----------|------------|-------------------------------|-------|---------|
| `codex-on-dark.png` | Codex (OpenAI) | OpenAI Blossom, white (`OAI_OpenAI-Blossom_White.png`) | [openai.com/brand](https://openai.com/brand/) > "Download logos" (`https://cdn.openai.com/brand/openai-logos.zip`) | [OpenAI brand guidelines and Marks usage terms](https://openai.com/brand/#usage-terms): the logo may be used only where it directly relates to OpenAI services, exactly as supplied, with credit that it belongs to OpenAI, never implying endorsement or shown more prominently than the app's own name. Permission can be withdrawn at any time. | None (byte-for-byte copy); scaled at run time |
| `codex-on-light.png` | Codex (OpenAI) | OpenAI Blossom, black (`OAI_OpenAI-Blossom_Black.png`), used in the Settings dialog and menus | same | same | None; scaled at run time |
| `antigravity-on-dark.png` | Antigravity (Google) | Google Antigravity icon, white (`antigravity-icon__white.png`) | [antigravity.google/press](https://www.antigravity.google/press), "Press Assets" (`/assets/image/brand/antigravity-icon__white.png`) | Official press asset offered for download by Google Antigravity; the page states no further usage terms, so it is used only to identify Antigravity's own data, unmodified. This is the Antigravity icon, not the Google or Gemini mark. | None; scaled at run time |
| `antigravity-on-light.png` | Antigravity (Google) | Google Antigravity icon, full colour (`antigravity-icon__full-color.png`) | same page (`/assets/image/brand/antigravity-icon__full-color.png`) | same | None; scaled at run time |
| `claude-on-dark.png`, `claude-on-light.png` | Claude Code (Anthropic) | **Not an Anthropic logo.** A neutral terminal glyph (rounded square with a `>_` prompt) drawn for PomoQuote | Original work of this project, CC0 1.0 | [Anthropic trademark guidelines](https://www.anthropic.com/legal/trademark-guidelines): the marks may be used only as specifically permitted by Anthropic and only in materials it approves beforehand, no alteration is allowed, and no logo files are offered for download. That excludes use here, so no Anthropic logo is shipped. | n/a |

### Decisions

- **Codex:** OpenAI publishes no separate Codex logo. The OpenAI Blossom identifies the OpenAI service (Codex) whose data is shown and is used unmodified, in the white and black versions OpenAI supplies, with the name "Codex" in the header, tooltip and accessible name. If OpenAI withdraws permission or the owner of this project prefers not to rely on it, delete `codex-on-dark.png` and `codex-on-light.png`: the widget then shows a neutral letter tile and the name.
- **Claude Code:** Anthropic's guidelines forbid using its logos without approval and provide no public asset, so PomoQuote shows a neutral icon plus the name "Claude" (tooltip, header, accessible name). If Anthropic approves the use in writing, replace the two `claude-*` files with the supplied images and record them here.
- **Antigravity:** the official Antigravity icon was chosen, not a Google or Gemini mark, because the widget shows Google Antigravity's usage and the press kit offers exactly this icon in a white and a full-colour version. Antigravity is not read by PomoQuote yet; the icon is packaged for when that provider is added.

Every file in `Assets/Providers/` must have a row above (a test checks this).
