#!/usr/bin/env node
// pre-push hook: no force-push, no direct push to a protected branch, and a fast backend test run when backend code changed.
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
import { git, gitOrNull, report, PROTECTED_BRANCHES } from './lib.mjs';

const ZERO = /^0+$/;
const problems = [];
// git passes: <local ref> <local sha> <remote ref> <remote sha> per line on stdin.
const lines = fs.readFileSync(0, 'utf8').split('\n').filter(Boolean);
for (const line of lines) {
  const [, localSha, remoteRef, remoteSha] = line.split(' ');
  const branch = remoteRef.replace('refs/heads/', '');
  if (ZERO.test(localSha)) {
    if (PROTECTED_BRANCHES.includes(branch)) problems.push(`Deleting the protected branch "${branch}" is not allowed.`);
    continue;
  }
  const isNewRemoteBranch = ZERO.test(remoteSha);
  if (!isNewRemoteBranch && gitOrNull('merge-base', '--is-ancestor', remoteSha, localSha) === null)
    problems.push(`${branch}: this push would rewrite history (not a fast-forward). Force-pushing is blocked; rebase onto the remote and push again.`);
  // Protected branches are changed through pull requests.
  if (PROTECTED_BRANCHES.includes(branch) && process.env.ALLOW_PUSH_MAIN !== '1')
    problems.push(`${branch} is protected: push a branch and open a pull request. (Override for an emergency: ALLOW_PUSH_MAIN=1.)`);
}
if (report('Push blocked by the pre-push guard:', problems)) process.exit(1);

// Fast checks that need no database (EF InMemory): only when the pushed commits touch backend code.
const touchesBackend = lines.some((l) => {
  const [, localSha, , remoteSha] = l.split(' ');
  if (ZERO.test(localSha)) return false;
  const base = ZERO.test(remoteSha) ? gitOrNull('merge-base', localSha, 'origin/master') : remoteSha;
  if (!base) return true;
  return git('diff', '--name-only', `${base}..${localSha}`).split('\n').some((f) => f.startsWith('backend/'));
});
if (process.env.SKIP_PREPUSH_TESTS !== '1' && touchesBackend) {
  const project = 'backend/tests/Application.Tests';
  if (fs.existsSync(project)) {
    console.log(`pre-push: dotnet test ${project}`);
    const r = spawnSync('dotnet', ['test', project, '--nologo', '-v', 'q'], { stdio: 'inherit', shell: process.platform === 'win32' });
    if (r.status !== 0) { console.error(`\nPush blocked: ${project} failed. Fix it, or SKIP_PREPUSH_TESTS=1 to push anyway (CI will still run it).`); process.exit(1); }
  }
}
