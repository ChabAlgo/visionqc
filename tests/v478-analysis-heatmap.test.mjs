import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const js = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');
const css = readFileSync(resolve(root, 'visionqc-extension.css'), 'utf8');

test('analysis score Viewer can generate and display a Green Heatmap with the preloaded Runtime', () => {
  assert.match(js, /modal-generate-heatmap/);
  assert.match(js, /function buildModalHeatmapInspectionRequest/);
  assert.match(js, /request\.green\.heatmapImageSave = true/);
  assert.match(js, /agentFetch\('\/api\/classification\/inspect'/);
  assert.match(js, /const tools = record\?\.tools \|\| record\?\.Tools \|\| \{\}/);
  assert.match(js, /!response\?\.ok \|\| !response\?\.dataUrl/);
  assert.match(css, /\.vq43-modal-heatmap-create\{/);
});
