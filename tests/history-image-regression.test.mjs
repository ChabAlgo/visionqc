import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const js = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');

test('CSV FullPath is retained and used before manually selected Actual NG images', () => {
  assert.match(js, /function csvFullPathValue/);
  assert.match(js, /function findFullPathColumn/);
  assert.match(js, /fullPath: fullPathIndex >= 0/);
  assert.match(js, /function csvImagesForRecord/);
  assert.match(js, /CSV FullPath Image/);
  assert.match(js, /\/api\/image\/preview/);
  const start = js.indexOf('function csvFullPathValue');
  const end = js.indexOf('function findFullPathColumn', start);
  const csvFullPathValue = new Function(`${js.slice(start, end)}; return csvFullPathValue;`)();
  assert.equal(csvFullPathValue('C:\\Images\\sample.jpg'), 'C:\\Images\\sample.jpg');
  assert.equal(csvFullPathValue('\\\\server\\share\\sample.png'), '\\\\server\\share\\sample.png');
  assert.equal(csvFullPathValue('relative/sample.jpg'), '');
});

test('CSV analysis history is explicit, chunked, and uses the active naming profile', () => {
  const save = js.slice(js.indexOf('async function saveCsvAnalysisHistory'), js.indexOf('async function chooseNgFolder'));
  assert.match(save, /\/api\/history\/import/);
  assert.match(save, /batch\.length >= 200/);
  assert.match(save, /namingProfile:state\.namingProfile/);
  assert.match(save, /원본 이미지는 복사하지 않고/);
  assert.match(js, /data-vq-action="save-csv-history"/);
});

test('Heatmap overlay save controls are Green-only, including Integrated mode', () => {
  const filter = js.slice(js.indexOf('function greenFilterOptions'), js.indexOf('function greenRuntimeOptions'));
  const runtime = js.slice(js.indexOf('function greenRuntimeOptions'), js.indexOf('function detectedGreenToolNames'));
  assert.doesNotMatch(filter, /integrated','heatmapImageSave/);
  assert.match(runtime, /function greenRuntimeOptions\(integrated=false\)/);
  assert.match(runtime, /NG 원본 위 Heatmap Overlay 저장/);
  assert.match(runtime, /!integrated/);
});
