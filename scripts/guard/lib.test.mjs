// Run with: node --test scripts/guard
import test from 'node:test';
import assert from 'node:assert/strict';
import { checkCommitMessage, checkBranchName, forbiddenPath, findSecrets, MAX_SUBJECT, PROTECTED_BRANCHES, GENERATED_PATHSPECS, git } from './lib.mjs';

const merged = { fromTargetHistory: true };
/** A valid conventional subject of exactly `length` characters. */
const subjectOf = (length) => `fix: ${'a'.repeat(length - 5)}`;

test('a local commit of 72 characters passes, 73 fails', () => {
  assert.deepEqual(checkCommitMessage(subjectOf(MAX_SUBJECT)), []);
  assert.notDeepEqual(checkCommitMessage(subjectOf(MAX_SUBJECT + 1)), []);
});

test('a merged commit of 72 characters plus " (#80)" passes', () => {
  assert.deepEqual(checkCommitMessage(`${subjectOf(72)} (#80)`, merged), []);
});

test('a merged commit of 73 characters before the suffix fails', () => {
  assert.notDeepEqual(checkCommitMessage(`${subjectOf(73)} (#80)`, merged), []);
});

test('the same subject fails without the merged-commit option (local hook, PR title)', () => {
  assert.notDeepEqual(checkCommitMessage(`${subjectOf(72)} (#80)`), []);
  assert.deepEqual(checkCommitMessage(subjectOf(72)), []);
});

test('only the trailing suffix is stripped, once', () => {
  assert.notDeepEqual(checkCommitMessage(`${subjectOf(72)} (#79) (#80)`, merged), []);
  assert.deepEqual(checkCommitMessage(`${subjectOf(60)} (#79) (#80)`, merged), []);
});

test('other rules stay strict for merged commits', () => {
  assert.notDeepEqual(checkCommitMessage('Add a thing (#80)', merged), []);
  assert.notDeepEqual(checkCommitMessage('fix: thing. (#80)', merged), []);
});

test('conventional commits: scope, breaking marker and git-generated messages', () => {
  assert.deepEqual(checkCommitMessage('feat(worklog)!: remove clock-in/out (shift log) feature'), []);
  assert.deepEqual(checkCommitMessage('Merge branch "x" into y'), []);
  assert.notDeepEqual(checkCommitMessage('updated stuff'), []);
  assert.notDeepEqual(checkCommitMessage('feat: subject\nbody without a blank line'), []);
});

test('an AI co-author trailer is allowed by default and rejected only when the project opts in', () => {
  const msg = 'feat: thing\n\nCo-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>';
  delete process.env.GUARD_REJECT_AI_COAUTHOR;
  assert.deepEqual(checkCommitMessage(msg), []);
  process.env.GUARD_REJECT_AI_COAUTHOR = '1';
  try { assert.notDeepEqual(checkCommitMessage(msg), []); } finally { delete process.env.GUARD_REJECT_AI_COAUTHOR; }
});

test('master is the protected branch and branch names follow <type>/<name>', () => {
  assert.deepEqual(PROTECTED_BRANCHES, ['master']);
  for (const ok of ['feat/payroll-run', 'feature/x', 'fix/login-429', 'chore/ci-cd-ims', 'docs/ims-team', 'dependabot/npm_and_yarn/x-1.2'])
    assert.deepEqual(checkBranchName(ok), [], ok);
  for (const bad of ['payroll', 'Feat/x', 'feat/', 'wip/stuff', 'feat/has space'])
    assert.notDeepEqual(checkBranchName(bad), [], bad);
});

test('forbidden paths: secrets, keys, backups and build output, but not .env.example', () => {
  for (const f of ['.env', '.env.production', 'certs/site.pem', 'db/dump.bak', 'backend/src/Api/bin/x.dll', 'dist/main.js', 'logs/app.log', 'coverage/lcov.info'])
    assert.equal(forbiddenPath(f), true, f);
  for (const f of ['.env.example', 'projects/admin-app/src/app/app.ts', 'backend/src/Api/appsettings.json'])
    assert.equal(forbiddenPath(f), false, f);
});

test('secret scan flags real-looking secrets and skips placeholders and allow-listed files', () => {
  assert.notDeepEqual(findSecrets('backend/src/X.cs', 'var k = "-----BEGIN RSA PRIVATE KEY-----";'), []);
  assert.notDeepEqual(findSecrets('backend/src/X.cs', 'Host=db;Password=SuperSecret99;'), []);
  assert.deepEqual(findSecrets('backend/src/X.cs', 'Password=${POSTGRES_PASSWORD}'), []);
  assert.deepEqual(findSecrets('docker-compose.yml', 'JWT_SECRET: "${JWT_SECRET:-dev}"'), []);
  assert.deepEqual(findSecrets('backend/tests/X.cs', 'Password=SuperSecret99;'), []);
  assert.deepEqual(findSecrets('backend/src/X.cs', 'Password=SuperSecret99; // guard:allow-secret test value'), []);
});

test('the repo-root package-lock.json and EF designer or snapshot files do not count towards the size limit', () => {
  const counted = git('ls-files', '--', '.', ...GENERATED_PATHSPECS).split('\n');
  const all = git('ls-files').split('\n');
  assert.ok(all.includes('package-lock.json'), 'the repo has a root lock file to exclude');
  assert.ok(!counted.includes('package-lock.json'), 'the root lock file must be excluded');
  assert.ok(!counted.some((f) => /\/Migrations\/.*(\.Designer\.cs|ModelSnapshot\.cs)$/.test(f)), 'EF generated files must be excluded');
  assert.ok(counted.includes('scripts/guard/lib.mjs'), 'ordinary source files stay counted');
});
