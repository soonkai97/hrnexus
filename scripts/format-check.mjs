// Prettier ratchet for pull requests: checks only the files a PR changes, so the repo is never mass-reformatted.
//   - a NEW file must be formatted;
//   - a MODIFIED file that was already formatted at the base must stay formatted;
//   - a MODIFIED file that was not formatted at the base is legacy and is skipped (reported, not enforced).
// Usage: BASE_SHA=<base commit or ref> node scripts/format-check.mjs   (HEAD is the working tree)
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import * as prettier from 'prettier';

const CHECKED = /\.(ts|html|scss|json|md|ya?ml|mjs|js)$/;
const SKIPPED = /(^|\/)(package-lock\.json|dist|node_modules|coverage)(\/|$)|\/Migrations\//;

export function decide(status, baseFormatted, headFormatted) {
  if (headFormatted) return 'ok';
  if (status === 'A' || status === 'R' || status === 'C') return 'fail';
  return baseFormatted ? 'fail' : 'legacy';
}

async function isFormatted(source, filepath) {
  const config = (await prettier.resolveConfig(filepath)) ?? {};
  try {
    return await prettier.check(source, { ...config, filepath });
  } catch {
    return false;
  }
}

const git = (...args) =>
  execFileSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });

async function main() {
  const base = process.env.BASE_SHA;
  if (!base) {
    console.error('BASE_SHA is required (for example BASE_SHA=origin/master).');
    process.exit(2);
  }
  const changed = git('diff', '--name-status', '--diff-filter=ACMR', base, '--', '.')
    .split('\n')
    .filter(Boolean)
    .map((line) => {
      const parts = line.split('\t');
      return { status: parts[0][0], file: parts[parts.length - 1] };
    })
    .filter(({ file }) => CHECKED.test(file) && !SKIPPED.test(file) && fs.existsSync(file));

  const failures = [];
  const legacy = [];
  for (const { status, file } of changed) {
    const headOk = await isFormatted(fs.readFileSync(file, 'utf8'), file);
    let baseOk = false;
    if (!headOk && status === 'M') {
      try {
        baseOk = await isFormatted(git('show', `${base}:${file}`), file);
      } catch {
        baseOk = false;
      }
    }
    const verdict = decide(status, baseOk, headOk);
    if (verdict === 'fail') failures.push(file);
    if (verdict === 'legacy') legacy.push(file);
  }

  if (legacy.length) {
    console.log(
      `Skipped ${legacy.length} legacy file(s) that were not Prettier-formatted before this PR:`,
    );
    legacy.forEach((f) => console.log(`  ${f}`));
  }
  if (failures.length) {
    console.error(`Prettier check failed for ${failures.length} file(s):`);
    failures.forEach((f) => console.error(`  ${f}`));
    console.error('Fix with: npx prettier --write <file>');
    process.exit(1);
  }
  console.log(`Prettier check passed (${changed.length} changed file(s) examined).`);
}

if (process.argv[1]?.split('\\').join('/').endsWith('/scripts/format-check.mjs')) {
  await main();
}
