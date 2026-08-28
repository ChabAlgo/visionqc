import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, name), 'utf8');

test('v4.7.15 viewer fits images and navigates heatmaps', () => {
  const js = read('visionqc-extension.js');
  const css = read('visionqc-extension.css');
  assert.match(js, /const VERSION = '4\.7\.15'/);
  assert.match(js, /const navigationImages = selectedOverlay \? overlayImages : displayImages/);
  assert.match(js, /if \(state\.modalOverlayPath\) \{[\s\S]*overlays\.findIndex/);
  assert.match(css, /vq43-modal-media-layer img\{width:auto!important;height:auto!important;max-width:100%!important;max-height:100%!important/);
});

test('history auto fetch is attempted once and chart uses full compact width', () => {
  const js = read('visionqc-extension.js');
  assert.match(js, /historyAttempted: false/);
  assert.match(js, /!state\.historyAttempted\) setTimeout\(\(\) => refreshHistory\(false, false\)/);
  assert.match(js, /timeout:30000/);
  assert.match(js, /rows\.length === 1[\s\S]*width-right/);
});

test('classification ignores stale object URL loads', () => {
  const base = read('assets/index-v4.4.33.js');
  assert.match(base, /VQ42_urlOwners=Ee\.useRef\(new Map\)/);
  assert.match(base, /VQ42_urlOwners\.current\.set\(ge,i\.id\)/);
  assert.match(base, /VQ42_urlOwners\.current\.get\(ge\.currentTarget\.src\)!==i\.id\)return/);
});

test('main and analysis layouts are compact and readable', () => {
  const js = read('visionqc-extension.js');
  const css = read('visionqc-extension.css');
  assert.match(js, /CSV<br>매칭/);
  assert.match(js, /정상<br>검출/);
  assert.match(js, /vq43-analysis-control-grid/);
  assert.match(css, /vq43-tool-position-grid\{grid-template-columns:repeat\(2,minmax\(0,1fr\)\)!important/);
  assert.match(css, /vq43-main-dashboard\{row-gap:0!important/);
  assert.match(js, /vq43-main-column-left/);
});

test('Agent suppresses only expected client disconnect alarms', () => {
  const cs = read('LocalAgent_v0.2.12/AgentServer.cs');
  assert.match(cs, /if \(!IsExpectedClientDisconnect\(ex\)\)/);
  assert.match(cs, /ex is IOException && ex\.InnerException is SocketException/);
});
