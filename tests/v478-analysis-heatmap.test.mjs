import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const js = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');
const css = readFileSync(resolve(root, 'visionqc-extension.css'), 'utf8');

test('analysis score Viewer displays only already-saved Green Heatmap overlays', () => {
  assert.match(js, /function overlayImagesForRecord/);
  assert.match(js, /data-vq-action="modal-overlay-image"/);
  assert.match(js, /OverlayPath/);
  assert.doesNotMatch(js, /modal-generate-heatmap/);
  assert.doesNotMatch(js, /function buildModalHeatmapInspectionRequest/);
  assert.doesNotMatch(js, /function generateModalHeatmap/);
  assert.doesNotMatch(css, /\.vq43-modal-heatmap-create\{/);
});