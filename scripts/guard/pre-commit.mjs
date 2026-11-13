#!/usr/bin/env node
// pre-commit hook: no direct commits to a protected branch, branch naming, no forbidden/large files, no secrets,
// no whitespace errors, and ESLint on the staged Angular files.
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
import { git, gitOrNull, checkBranchName, forbiddenPath, findSecrets, report, MAX_FILE_BYTES, PROTECTED_BRANCHES } from './lib.mjs';

const problems = [];
const branch = gitOrNull('rev-parse', '--abbrev-ref', 'HEAD') ?? '';
const hasCommits = gitOrNull('rev-parse', '--verify', 'HEAD') !== null;

// Work happens on short-lived branches and reaches master through a pull request.
if (hasCommits && PROTECTED_BRANCHES.includes(branch) && process.env.ALLOW_MAIN_COMMIT !== '1')
  problems.push(`You are on "${branch}", which is protected: create a branch first (git switch -c feat/<name>) and open a pull request. (Override for an emergency: ALLOW_MAIN_COMMIT=1.)`);
if (hasCommits && branch !== 'HEAD') problems.push(...checkBranchName(branch));

const staged = git('diff', '--cached', '--name-only', '--diff-filter=ACMR').split('\n').filter(Boolean);
for (const file of staged) {
  if (forbiddenPath(file)) problems.push(`${file}: this kind of file must not be committed (secrets, keys, backups, build output, logs).`);
  const size = fs.existsSync(file) ? fs.statSync(file).size : 0;
  if (size > MAX_FILE_BYTES) problems.push(`${file}: ${(size / 1048576).toFixed(1)} MB is over the ${MAX_FILE_BYTES / 1048576} MB limit (use an artifact or release asset).`);
  if (size > 0 && size <= MAX_FILE_BYTES && !forbiddenPath(file)) {
    const text = fs.readFileSync(file, 'utf8');
    if (!text.includes('\u0000')) problems.push(...findSecrets(file, text));
  }
}
const whitespace = gitOrNull('diff', '--cached', '--check');
if (whitespace) problems.push(...whitespace.split('\n').filter((l) => /trailing whitespace|leftover conflict marker/.test(l)).slice(0, 10));

if (report('Commit blocked by the pre-commit guard:', problems)) process.exit(1);

// Angular files: lint only what is staged, when dependencies are installed. Warnings are allowed, errors block (same as CI).
const lintable = staged.filter((f) => /^projects\/.+\.(ts|html)$/.test(f) && fs.existsSync(f));
if (lintable.length > 0 && fs.existsSync('node_modules/.bin')) {
  const r = spawnSync('npx', ['--no-install', 'eslint', '--no-warn-ignored', ...lintable], { stdio: 'inherit', shell: process.platform === 'win32' });
  if (r.status !== 0) process.exit(1);
}
