import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { FIXTURE, OUTPUT, renderFormatted } from './render-payroll-worked-examples.mjs';

// WHY: the accountant signs off the generated pack, not the fixture. If someone edits an example and forgets to
// regenerate, the pack the accountant read would no longer be what the program is tested against.
test('the accountant review pack is up to date with the worked-examples fixture', async () => {
  const fixture = JSON.parse(fs.readFileSync(FIXTURE, 'utf8'));
  const expected = await renderFormatted(fixture);
  const actual = fs.readFileSync(OUTPUT, 'utf8');
  assert.equal(
    actual,
    expected,
    'docs/process/payroll-worked-examples.md is stale: run `node scripts/render-payroll-worked-examples.mjs`',
  );
});

test('every example shows its working and an expected net pay and employer cost', () => {
  const fixture = JSON.parse(fs.readFileSync(FIXTURE, 'utf8'));
  for (const v of fixture.vectors) {
    assert.ok(Array.isArray(v.working) && v.working.length > 0, `"${v.name}" has no working shown`);
    assert.equal(typeof v.expected.net, 'number', `"${v.name}" has no expected net pay`);
    assert.equal(
      typeof v.expected.employerCost,
      'number',
      `"${v.name}" has no expected employer cost`,
    );
  }
});

test('the generated pack is rendered without the machine locale (decimal point, not comma)', async () => {
  const fixture = JSON.parse(fs.readFileSync(FIXTURE, 'utf8'));
  const text = await renderFormatted(fixture);
  assert.match(text, /RM3,478\.50/); // comma groups thousands, a point is the decimal separator
  assert.doesNotMatch(text, /RM3\.478,50/); // never the continental form
});
