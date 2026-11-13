// Shared rules for the git guards (local hooks and the PR guard workflow). Plain Node, no dependencies.
// Ported from the IMS standard (github.com/soonkai97/IMS) and adapted to HRNexus (default branch: master).
import { execFileSync } from 'node:child_process';

export const git = (...args) => execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
export const gitOrNull = (...args) => { try { return git(...args); } catch { return null; } };

export const PROTECTED_BRANCHES = ['master'];
export const BRANCH_PATTERN = /^(feat|feature|fix|chore|docs|refactor|test|ci|perf|hotfix|release|dependabot)\/[A-Za-z0-9._\-/]+$/;
export const COMMIT_TYPES = ['feat', 'fix', 'docs', 'style', 'refactor', 'perf', 'test', 'build', 'ci', 'chore', 'revert'];
export const CONVENTIONAL = new RegExp(`^(${COMMIT_TYPES.join('|')})(\\([a-z0-9._/-]+\\))?!?: \\S.{0,70}$`);
export const MAX_SUBJECT = 72;
export const MAX_FILE_BYTES = 2 * 1024 * 1024;
export const MAX_PR_CHANGED_LINES = 1500;
export const LARGE_CHANGE_LABEL = 'large-change';

/** Generated files (lock files, EF Core designer and snapshot files) do not count towards the PR size limit. */
export const GENERATED_PATHSPECS = [':(exclude)package-lock.json', ':(exclude)**/package-lock.json', ':(exclude)**/Migrations/*.Designer.cs', ':(exclude)**/Migrations/*ModelSnapshot.cs'];

/** Messages git generates itself are not rewritten by people. */
export const isGeneratedMessage = (subject) => /^(Merge |Revert "|fixup! |squash! |amend! )/.test(subject);

/**
 * IMS rejects AI co-author trailers. HRNexus history and tooling already carry them, so the check is OFF here by
 * default. Set GUARD_REJECT_AI_COAUTHOR=1 (hooks) or the same variable in the workflow to enforce the IMS rule.
 */
export const AI_COAUTHOR = /^co-authored-by:.*(claude|anthropic|noreply@anthropic\.com)/im;
export const rejectAiCoauthor = () => process.env.GUARD_REJECT_AI_COAUTHOR === '1';

/** GitHub's squash-merge appends " (#123)" to the PR title; the author does not control it. */
const SQUASH_SUFFIX = / \(#\d{1,7}\)$/;

/**
 * `fromTargetHistory`: the commit was squash-merged on GitHub, so one trailing " (#123)" does not count towards the
 * subject limit. Only for commits that arrive through a PR range; the PR title and local commits are checked in full.
 */
export function checkCommitMessage(message, { fromTargetHistory = false } = {}) {
  const problems = [];
  const lines = message.replace(/\r/g, '').split('\n');
  const trimmed = (lines[0] ?? '').trim();
  const subject = fromTargetHistory ? trimmed.replace(SQUASH_SUFFIX, '') : trimmed;
  if (isGeneratedMessage(subject)) return problems;
  if (!CONVENTIONAL.test(subject))
    problems.push(`Subject must follow Conventional Commits: "type(scope): summary" with type one of ${COMMIT_TYPES.join(', ')} (max ${MAX_SUBJECT} chars). Got: "${subject}"`);
  if (subject.length > MAX_SUBJECT) problems.push(`Subject is ${subject.length} characters; the limit is ${MAX_SUBJECT}.`);
  if (/[.]$/.test(subject)) problems.push('Subject must not end with a full stop.');
  if (lines.length > 1 && lines[1].trim() !== '') problems.push('Leave a blank line between the subject and the body.');
  if (rejectAiCoauthor() && AI_COAUTHOR.test(message)) problems.push('Remove the AI "Co-Authored-By" trailer: this project is configured to reject it (GUARD_REJECT_AI_COAUTHOR=1).');
  return problems;
}

export function checkBranchName(name) {
  if (PROTECTED_BRANCHES.includes(name)) return [];
  return BRANCH_PATTERN.test(name) ? [] : [`Branch "${name}" must be named <type>/<short-description>, type one of feat, feature, fix, chore, docs, refactor, test, ci, perf, hotfix, release. Example: feat/payroll-run`];
}

const FORBIDDEN_PATHS = [
  /(^|\/)\.env(\.|$)(?!example)/, /\.(pfx|p12|pem|key|jks|keystore)$/i, /\.(bak|trn|mdf|ldf|dump)$/i, /\.sql\.gz$/i,
  /(^|\/)appsettings\.Production\.json$/i, /(^|\/)(bin|obj|node_modules|logs|coverage|dist|playwright-report|test-results|tmp-e2e-capture)\//,
  /(^|\/)artifacts\//, /\.local\.json$/i, /(^|\/)docker-build-[a-z]+\.log$/, /(^|\/)maintenance\/on$/,
];
export const forbiddenPath = (file) => FORBIDDEN_PATHS.some((re) => re.test(file.replaceAll('\\', '/')));

const SECRET_PATTERNS = [
  ['private key block', /-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----/],
  ['AWS access key id', /\bAKIA[0-9A-Z]{16}\b/],
  ['GitHub token', /\bgh[pousr]_[A-Za-z0-9]{36,}\b/],
  ['Slack token', /\bxox[baprs]-[A-Za-z0-9-]{10,}\b/],
  ['JWT', /\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\b/],
  ['connection string with a password', /\b(?:Password|Pwd)\s*=\s*(?!\$\{|<|\{|%|;|["']?\s*$)[^;"'\s]{6,}/i],
  ['token or key assignment', /\b(?:token|private[_-]?key|jwt[_-]?secret|pii[_-]?encryption[_-]?key)\s*[:=]\s*["']?[A-Za-z0-9+/_-]{24,}/i],
];
/** Files that may legitimately contain throw-away values (documented in the file itself). */
const SECRET_ALLOW = [
  /(^|\/)appsettings\.Development\.json$/, /^backend\/src\/Api\/appsettings\.json$/, /^docs\//, /^\.github\/workflows\/[^/]+\.yml$/,
  /^scripts\/guard\//, /^tests\//, /^backend\/tests\//, /\.spec\.ts$/, /\.md$/, /^\.env\.example$/,
];
export function findSecrets(file, text) {
  const f = file.replaceAll('\\', '/');
  if (SECRET_ALLOW.some((re) => re.test(f))) return [];
  const found = [];
  text.split('\n').forEach((line, i) => {
    if (line.includes('guard:allow-secret')) return;
    for (const [name, re] of SECRET_PATTERNS) if (re.test(line)) found.push(`${f}:${i + 1}  looks like a ${name}`);
  });
  return found;
}

export function report(title, problems) {
  if (problems.length === 0) return 0;
  console.error(`\n${title}`);
  for (const p of problems) console.error(`  - ${p}`);
  console.error('');
  return 1;
}
