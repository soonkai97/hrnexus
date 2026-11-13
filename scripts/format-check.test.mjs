import test from 'node:test';
import assert from 'node:assert/strict';
import { decide } from './format-check.mjs';

test('a formatted file always passes', () => {
  assert.equal(decide('A', false, true), 'ok');
  assert.equal(decide('M', false, true), 'ok');
});

test('a new unformatted file fails', () => {
  assert.equal(decide('A', false, false), 'fail');
  assert.equal(decide('R', false, false), 'fail');
});

test('a file that was formatted at the base must stay formatted', () => {
  assert.equal(decide('M', true, false), 'fail');
});

test('a legacy unformatted file that is only modified is skipped, not forced to reformat', () => {
  assert.equal(decide('M', false, false), 'legacy');
});
