// Tint admin-app PWA icons green (#16a34a) to distinguish them from staff icons.
// Staff icons stay untouched. Admin icons get the same shape + transparency but
// a green colour overlay so managers can tell the two apps apart on the home screen.

const sharp = require('sharp');
const fs    = require('fs');
const path  = require('path');

const SRC = 'projects/staff-app/public/icons';
const DST = 'projects/admin-app/public/icons';

// Admin accent colour — green-600 (#16a34a), distinct from staff purple
const TINT_R = 22, TINT_G = 163, TINT_B = 74;

async function tintIcon(srcFile, dstFile) {
  const img   = sharp(srcFile).ensureAlpha();
  const meta  = await img.metadata();
  const { width, height } = meta;

  // Build a solid green overlay image at 60% opacity
  const overlayBuf = Buffer.alloc(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    overlayBuf[i * 4 + 0] = TINT_R;
    overlayBuf[i * 4 + 1] = TINT_G;
    overlayBuf[i * 4 + 2] = TINT_B;
    overlayBuf[i * 4 + 3] = Math.round(255 * 0.60); // 60% opacity
  }

  const overlay = {
    input: overlayBuf,
    raw: { width, height, channels: 4 },
    blend: 'over',
  };

  await sharp(srcFile)
    .ensureAlpha()
    .composite([overlay])
    .toFile(dstFile);
}

(async () => {
  if (!fs.existsSync(DST)) fs.mkdirSync(DST, { recursive: true });

  const files = fs.readdirSync(SRC).filter(f => f.endsWith('.png'));
  for (const f of files) {
    await tintIcon(path.join(SRC, f), path.join(DST, f));
    console.log('  tinted', f);
  }
  console.log('Done — admin icons are now green.');
})();
