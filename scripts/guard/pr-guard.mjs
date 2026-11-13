#!/usr/bin/env node
// PR guard (runs in CI on pull requests). Inputs come from environment variables, never from the command line,
// so a hostile PR title or branch name cannot inject anything into a shell.
//   PR_TITLE, PR_BRANCH, BASE_SHA, HEAD_SHA, PR_LABELS
import { execFileSync } from 'node:child_process';
import { checkCommitMessage, checkBranchName, forbiddenPath, findSecrets, report, MAX_PR_CHANGED_LINES, LARGE_CHANGE_LABEL, GENERATED_PATHSPECS, AI_COAUTHOR, rejectAiCoauthor } from './lib.mjs';

const { PR_TITLE = '', PR_BRANCH = '', BASE_SHA, HEAD_SHA, PR_LABELS = '' } = process.env;
const run = (...args) => execFileSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
const problems = [];

// Squash-merge uses the PR title as the commit on master, so it must be a conventional commit too.
problems.push(...checkCommitMessage(PR_TITLE).map((p) => `PR title: ${p}`));
problems.push(...checkBranchName(PR_BRANCH).map((p) => `Branch: ${p}`));

if (BASE_SHA && HEAD_SHA) {
  const range = `${BASE_SHA}...${HEAD_SHA}`;
  // Only the commits this PR adds: two dots. (Three dots would also list commits that exist only on the base branch.)
  // A commit already squash-merged into a branch this PR merges (an integration branch) carries GitHub's " (#123)" suffix.
  for (const sha of run('rev-list', '--no-merges', `${BASE_SHA}..${HEAD_SHA}`).split('\n').filter(Boolean)) {
    const message = run('log', '-1', '--format=%B', sha);
    problems.push(...checkCommitMessage(message.trim(), { fromTargetHistory: true }).map((p) => `Commit ${sha.slice(0, 7)}: ${p}`));
  }
  const files = run('diff', '--name-only', '--diff-filter=ACMR', range).split('\n').filter(Boolean);
  for (const f of files) {
    if (forbiddenPath(f)) problems.push(`${f}: must not be committed (secrets, keys, backups, build output, logs).`);
    const text = (() => { try { return run('show', `${HEAD_SHA}:${f}`); } catch { return ''; } })();
    if (!text.includes('\u0000')) problems.push(...findSecrets(f, text));
  }
  // Lock files and EF Core designer/snapshot files are generated, not reviewed line by line, so they do not count towards the size limit.
  const changed = run('diff', '--shortstat', range, '--', '.', ...GENERATED_PATHSPECS).match(/(\d+) insertions?|(\d+) deletions?/g)?.reduce((n, m) => n + Number.parseInt(m, 10), 0) ?? 0;
  const sizeApproved = PR_LABELS.split(',').includes(LARGE_CHANGE_LABEL);
  if (changed > MAX_PR_CHANGED_LINES && !sizeApproved) problems.push(`This PR changes ${changed} lines (limit ${MAX_PR_CHANGED_LINES}). Split it into smaller pull requests, or have the owner add the "${LARGE_CHANGE_LABEL}" label when a large change cannot be split (for example removing a whole feature).`);
  if (rejectAiCoauthor() && AI_COAUTHOR.test(PR_TITLE)) problems.push('PR title carries an AI co-author trailer.');
}
process.exit(report('PR guard failed:', problems));
