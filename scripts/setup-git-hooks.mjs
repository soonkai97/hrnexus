#!/usr/bin/env node
// One-time setup per clone: point git at the versioned hooks in .githooks/.   node scripts/setup-git-hooks.mjs
import { execFileSync } from 'node:child_process';

execFileSync('git', ['config', 'core.hooksPath', '.githooks'], { stdio: 'inherit' });
execFileSync('git', ['config', 'pull.ff', 'only'], { stdio: 'inherit' });
execFileSync('git', ['config', 'push.autoSetupRemote', 'true'], { stdio: 'inherit' });
execFileSync('git', ['config', 'merge.ff', 'false'], { stdio: 'inherit' });
console.log('Git hooks active (.githooks): pre-commit, commit-msg, pre-push. See docs/process/git-workflow.md.');
