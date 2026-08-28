import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, name), 'utf8');

test('Threshold input stays inside its Tool card while preserving native number spinner', () => {
  const css = read('visionqc-extension.css');
  assert.match(css, /vq43-threshold-input\{[\s\S]*?appearance:auto!important;[\s\S]*?width:48px!important/s);
  assert.match(css, /vq43-threshold-input\{[\s\S]*?height:30px!important/s);
});

test('analysis scope note is directly below filters and live rendering defers during pointer input', () => {
  const js = read('visionqc-extension.js');
  const css = read('visionqc-extension.css');
  assert.match(js, /vq43-analysis-left[\s\S]*vq43-filter[\s\S]*vq43-analysis-scope-note[\s\S]*vq43-kpi-grid/);
  assert.match(js, /function queueLiveUiRender\(\)/);
  assert.match(js, /state\.liveUiPointerActive/);
  assert.match(js, /queueLiveUiRender\(\);[\s\S]*updateSimulationStatusDom\(\)/);
  assert.match(css, /#vq43-global-sim-progress\{pointer-events:none!important\}/);
  assert.match(css, /#vq43-global-sim-progress>button\{pointer-events:auto!important\}/);
});

test('score viewer deduplicates source images and navigates in chart order', () => {
  const js = read('visionqc-extension.js');
  assert.match(js, /function mergeScoreViewerImages\(csvImages, actualImages\)/);
  assert.match(js, /actualNg:Boolean\(existing\.actualNg \|\| candidate\.actualNg\)/);
  assert.match(js, /scorePointIndex:pointIndex/);
  assert.match(js, /function changeScorePointImage\(delta\)/);
  assert.match(js, /openScorePointImage\(point\.key, \{ preserveView:true \}\)/);
  assert.match(js, /const points = sortAnalysisScorePoints\(scorePoints/);
});

test('score viewer uses the full viewport and contain fitting at 100 percent', () => {
  const css = read('visionqc-extension.css');
  assert.match(css, /vq43-modal-card\{width:calc\(100vw - 24px\)!important;height:calc\(100vh - 24px\)!important/);
  assert.match(css, /vq43-modal-media-layer img\{[\s\S]*?width:100%!important;[\s\S]*?height:100%!important;[\s\S]*?object-fit:contain!important/s);
});
