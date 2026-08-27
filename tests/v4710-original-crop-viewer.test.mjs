import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const web = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');
const agent = readFileSync(resolve(root, 'LocalAgent_v0.2.12', 'AgentServer.cs'), 'utf8');
const green = readFileSync(resolve(root, 'LocalAgent_v0.2.12', 'Engine', 'GreenOverlayProcessor.cs'), 'utf8');
const blue = readFileSync(resolve(root, 'LocalAgent_v0.2.12', 'Engine', 'BlueCropCore.cs'), 'utf8');
const store = readFileSync(resolve(root, 'LocalAgent_v0.2.12', 'Persistence', 'SqliteRunStore.cs'), 'utf8');

test('Integrated results keep original and processed Crop paths separately', () => {
  assert.match(green, /SourceFullPath/);
  assert.match(green, /FullPath = sourceFullPath/);
  assert.match(green, /ProcessingPath = job\.ImagePath/);
  assert.match(blue, /greenSession\.ProcessImage\(slot\.Key, result\.OutputPath, imagePath/);
  assert.match(store, /processed_path/);
});

test('Viewer defaults to source and exposes Crop plus Heatmap safely', () => {
  assert.match(web, /function findProcessedPathColumn/);
  assert.match(web, /function modalImagesForView/);
  assert.match(web, /function selectModalCropImage/);
  assert.match(web, /modalImageSwitcherHtml/);
  assert.match(web, /modalHeatmapCreateHtml/);
  assert.match(web, /Simulation 실행 중에는 Runtime을 공유할 수 없습니다/);
});

test('Single Green Heatmap accepts an Integrated-compatible preloaded Runtime', () => {
  const start = agent.indexOf('private object InspectSingleGreenImage');
  const end = agent.indexOf('private object InspectUploadedGreenImage', start);
  assert.ok(start >= 0 && end > start);
  assert.match(agent.slice(start, end), /HasCompatiblePreloadedRuntime\(req, signature\)/);
});