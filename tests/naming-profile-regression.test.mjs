import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const js = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');

test('파일명 규칙 설정은 Cell ID, 날짜, 시간의 위치/자동 모드를 모두 제공한다', () => {
  assert.match(js, /const NAMING_PROFILE_KEY = 'visionqc-v450-naming-profile'/);
  assert.match(js, /function namingProfileCardHtml\(\)/);
  assert.match(js, /후보 전체 길이/);
  assert.match(js, /앞에서 추출할 길이/);
  assert.match(js, /유효한 YYYYMMDD 토큰/);
  assert.match(js, /유효한 HHMMSS 토큰/);
  assert.match(js, /\/api\/naming\/preview/);
});

test('기본 Cell ID 규칙은 18글자 후보에서 앞 16글자를 사용한다', () => {
  const start = js.indexOf('const defaultNamingProfile');
  const end = js.indexOf('const asRuleMode', start);
  assert.ok(start >= 0 && end > start);
  const profile = new Function(`${js.slice(start, end)}; return defaultNamingProfile();`)();
  assert.equal(profile.cellId.candidateLength, 18);
  assert.equal(profile.cellId.extractLength, 16);
  assert.equal(profile.cellId.requireLetter, true);
  const candidate = 'J4037F2JP611069701';
  assert.equal(candidate.length, 18);
  assert.equal(candidate.slice(0, profile.cellId.extractLength), 'J4037F2JP6110697');
});
