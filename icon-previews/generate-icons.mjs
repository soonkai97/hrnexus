import sharp from 'sharp';
import { readFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dir = dirname(fileURLToPath(import.meta.url));
const svg   = readFileSync(join(__dir, 'option-2-purple-gradient.svg'));
const out   = join(__dir, '../projects/staff-app/public/icons');

const sizes = [72, 96, 128, 144, 152, 192, 384, 512];

for (const s of sizes) {
  await sharp(svg).resize(s, s).png().toFile(join(out, `icon-${s}x${s}.png`));
  console.log(`✓ icon-${s}x${s}.png`);
}

console.log('\nAll icons generated!');
