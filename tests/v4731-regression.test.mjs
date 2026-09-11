import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const js = fs.readFileSync(new URL('../visionqc-extension.js', import.meta.url), 'utf8');
const css = fs.readFileSync(new URL('../visionqc-extension.css', import.meta.url), 'utf8');

test('analysis viewer stores the same ascending Score order rendered by the graph', () => {
  const render = js.slice(js.indexOf('function renderAnalysis()'), js.indexOf('function histogramSvg'));
  assert.match(render, /const points = sortAnalysisScorePoints\(scorePoints/);
  assert.match(js, /scorePointIndex <= 0/);
  assert.match(js, /scorePointIndex >= scorePointCount - 1/);
});

test('daily NG chart reserves visual insets while retaining full-width hit targets', () => {
  const chart = js.slice(js.indexOf('function historyDateBars'), js.indexOf('function historyRecordRowsLegacy'));
  assert.match(chart, /const pointInset = rows\.length <= 1/);
  assert.match(chart, /const hitLeft =/);
  assert.match(chart, /const hitRight =/);
});

test('Runtime load merges discovered top-level Green and Red tools into the request', () => {
  assert.match(js, /function syncDetectedGreenTools/);
  assert.match(js, /const addedTools = syncDetectedGreenTools\(ensureSimulationForm\(\)\)/);
  assert.match(js, /\.filter\(tool => !String\(tool\.path \|\| ''\)\.includes\('\/'\)\)/);
});

test('dashboard Tool metadata wraps into rows with a readable Threshold control', () => {
  assert.match(css, /v4\.7\.31:[\s\S]*?vq43-tool-score-meta>span[\s\S]*?white-space:normal!important/);
  assert.match(css, /v4\.7\.31:[\s\S]*?vq43-tool-score-meta label[\s\S]*?grid-template-columns:1fr!important/);
  assert.match(css, /v4\.7\.31:[\s\S]*?vq43-threshold-input[\s\S]*?width:48px!important/);
});
