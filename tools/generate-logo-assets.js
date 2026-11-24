// Builds the PomoQuote logo assets from Assets/Logo/pomoquote-logo.svg:
//   Assets/Logo/pomoquote-logo-1024.png and Assets/Logo/pomoquote.ico (16, 20, 24, 32, 40, 48, 64, 256 px).
// Usage (from the repo root):  npm install --no-save sharp   then   node tools/generate-logo-assets.js [repoRoot]
// Each size is rendered from the vector, not scaled from a bigger bitmap, so the small sizes stay crisp.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const root = process.argv[2] || path.join(__dirname, '..'); // repo root (default: the folder above tools/)
const dir = path.join(root, 'Assets', 'Logo');
const svg = fs.readFileSync(path.join(dir, 'pomoquote-logo.svg'));
const sizes = [16, 20, 24, 32, 40, 48, 64, 256];

async function render(size) {
  // Render large and let the resampler reduce it: this gives the best edges for the small sizes.
  return sharp(svg, { density: 72 * (size < 64 ? 8 : 2) })
    .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 }, kernel: 'lanczos3' })
    .png({ compressionLevel: 9 })
    .toBuffer();
}

(async () => {
  const images = [];
  for (const size of sizes) images.push({ size, data: await render(size) });

  // ICO container with PNG-compressed images (supported since Windows Vista).
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); // reserved
  header.writeUInt16LE(1, 2); // type: icon
  header.writeUInt16LE(images.length, 4);
  const entries = Buffer.alloc(16 * images.length);
  let offset = 6 + entries.length;
  images.forEach((img, i) => {
    const e = i * 16;
    entries.writeUInt8(img.size >= 256 ? 0 : img.size, e); // width (0 = 256)
    entries.writeUInt8(img.size >= 256 ? 0 : img.size, e + 1); // height
    entries.writeUInt8(0, e + 2); // palette
    entries.writeUInt8(0, e + 3); // reserved
    entries.writeUInt16LE(1, e + 4); // planes
    entries.writeUInt16LE(32, e + 6); // bits per pixel
    entries.writeUInt32LE(img.data.length, e + 8);
    entries.writeUInt32LE(offset, e + 12);
    offset += img.data.length;
  });
  fs.writeFileSync(path.join(dir, 'pomoquote.ico'), Buffer.concat([header, entries, ...images.map((i) => i.data)]));

  await sharp(svg, { density: 72 * 4 }).resize(1024, 1024).png({ compressionLevel: 9 }).toFile(path.join(dir, 'pomoquote-logo-1024.png'));
  console.log('ico sizes:', sizes.join(','), 'bytes:', fs.statSync(path.join(dir, 'pomoquote.ico')).size);
})();
