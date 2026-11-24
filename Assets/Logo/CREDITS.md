# Logo credits

The PomoQuote logo is **original artwork** drawn for this project as a vector (`pomoquote-logo.svg`, plain SVG shapes). There is no third-party artwork, stock image, icon font or AI-generated image behind it. It is released under the same [MIT License](../../LICENSE) as the rest of the repository.

**Idea.** A clock ring split into a warm half (focus) and a cool half (rest) around a white disc holding a large quotation mark (the sentence of the day), on a dark rounded square. The tick at the top is the 12 o'clock mark. It reads at 16 px as an orange/teal ring with a dark quote mark and stays visible on light and dark taskbars (the square has a light edge for dark backgrounds).

**Files**

- `pomoquote-logo.svg`: the source.
- `pomoquote-logo-1024.png`: 1024 px rendering of the source.
- `pomoquote.ico`: Windows icon with 16, 20, 24, 32, 40, 48, 64 and 256 px images (32-bit, PNG-compressed). It is the executable's icon (`<ApplicationIcon>` in `PomoQuote.csproj`) and is embedded in the assembly for the window, taskbar, tray and About dialog (`AppIcon.cs`).
- `preview-*.png`: screenshots of the real title bar and About dialog, and the icon at 100/125/150/200 % display scale on light and dark taskbars (neutral mock taskbars, no desktop content).

**Regenerating.** Edit the SVG, then run `npm install --no-save sharp` and `node tools/generate-logo-assets.js` from the repository root. Every size is rendered from the vector, so small sizes stay sharp. `PomoQuote.Tests/AppIconTests.cs` checks that the icon file has all eight sizes and that the embedded copy is the file in the repository.
