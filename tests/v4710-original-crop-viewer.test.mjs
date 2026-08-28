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

test('Viewer defaults to inspection original and exposes Crop plus saved Heatmap safely', () => {
  assert.match(web, /function findProcessedPathColumn/);
  assert.match(web, /function modalImagesForView/);
  assert.match(web, /function selectModalCropImage/);
  assert.match(web, /modalImageSwitcherHtml/);
  assert.match(web, /function originalPathForViewer/);
  assert.match(web, /historyLookupPath/);
  assert.doesNotMatch(web, /modal-generate-heatmap/);
});
test('Single Green Heatmap accepts an Integrated-compatible preloaded Runtime', () => {
  const start = agent.indexOf('private object InspectSingleGreenImage');
  const end = agent.indexOf('private object InspectUploadedGreenImage', start);
  assert.ok(start >= 0 && end > start);
  assert.match(agent.slice(start, end), /HasCompatiblePreloadedRuntime\(req, signature\)/);
});

test('Blue save options include explicit tooltips', () => {
  assert.match(web, /saveAsJpeg:/);
  assert.match(web, /skipExisting:/);
  assert.match(web, /data-vq-tooltip/);
});

test('Saved Source tags restore an old Integrated Crop path to its configured Grab root', () => {
  const helperStart = web.indexOf('function configuredOriginalImageRootsForViewer');
  const helperEnd = web.indexOf('function modalImagesForView', helperStart);
  const roots = ['H:\\내 드라이브\\1.Grab\\_IMG\\0.Test\\_set\\BAN\\1Line\\_BAN\\0202\\15', 'H:\\내 드라이브\\1.Grab\\_IMG\\0.Test\\_set\\BAN\\1Line\\_BAN\\0202\\16'];
  const originalPathForViewer = new Function('ensureSimulationForm', 'normalizeImageRoots', 'imageRootsForPosition', web.slice(helperStart, helperEnd) + '; return originalPathForViewer;')(() => ({ integrated:{ keywordInputRoots:roots }, green:{}, positions:{} }), (value) => Array.isArray(value) ? value : [String(value || '')].filter(Boolean), () => []);  const cropPath = 'C:\\Temp\\Out\\_VisionQC_Integrated_Images\\AN(TOP)\\Source_01_15\\20260202_154148_P163GG22M210083116_TN1105_OK_CAM2_Blue.jpg';
  const sourcePath = 'H:\\내 드라이브\\1.Grab\\_IMG\\0.Test\\_set\\BAN\\1Line\\_BAN\\0202\\15\\20260202_154148_P163GG22M210083116_TN1105_OK_CAM2_Blue.jpg';
  assert.equal(originalPathForViewer(cropPath), sourcePath);
});