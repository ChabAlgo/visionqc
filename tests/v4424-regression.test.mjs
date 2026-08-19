import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const html = read('index.html');
const js = read('visionqc-extension.js');
const css = read('visionqc-v4424-clean.css');

test('v4.4.24 assets are versioned and loaded in override order', () => {
  assert.match(html, /VisionQC DirectExport v4\.4\.24/);
  assert.match(html, /assets\/index-v4\.4\.24\.js/);
  assert.match(html, /visionqc-extension\.js\?v=4\.4\.24/);
  assert.match(html, /visionqc-v4424-clean\.css\?v=4\.4\.24/);
  assert.ok(html.indexOf('visionqc-v4424-clean.css') > html.indexOf('visionqc-extension.css'));
  assert.ok(existsSync(resolve(root, 'assets/index-v4.4.24.js')));
  assert.ok(existsSync(resolve(root, 'visionqc-v4424-clean.css')));
});

test('expanded drawer shifts the FHD workspace instead of covering it', () => {
  assert.match(css, /--vq-rail-open:272px/);
  assert.match(css, /body\.vq43-menu-expanded #vq43-shell\{left:var\(--vq-rail-open\)!important/);
  assert.match(css, /body\.vq43-menu-expanded\[data-vq-page="classification"\]/);
  assert.match(css, /transition:left \.2s ease,width \.2s ease/);
});

test('Simulation Options uses a dedicated inner scroller', () => {
  assert.match(js, /class="vq43-sim-options-scroll"/);
  assert.match(js, /\$\('\.vq43-sim-options-scroll'\) \|\| \$\('\.vq43-sim-options'\)/);
  assert.match(css, /\.vq43-sim-options\{[^}]*overflow:hidden!important/);
  assert.match(css, /\.vq43-sim-options-scroll\{[^}]*overflow-y:auto!important/);
  assert.match(css, /overflow-anchor:none!important/);
  assert.doesNotMatch(css, /contain:layout paint/);
});

test('Tool Settings fits its panel without horizontal dragging', () => {
  assert.match(js, /class="vq43-sim-table-wrap vq43-sim-tools-wrap"/);
  assert.match(js, /class="vq43-tool-col-threshold"/);
  assert.match(css, /\.vq43-sim-tools-wrap\{overflow-x:hidden!important\}/);
  assert.match(css, /\.vq43-sim-table\.tools\{[^}]*table-layout:fixed!important/);
  assert.match(css, /\.vq43-sim-table\.tools\{[^}]*min-width:0!important/);
  assert.doesNotMatch(css, /\.vq43-sim-table\.tools\{min-width:620px!important\}/);
});

test('FHD stays two-column and narrower viewports fall back to one column', () => {
  assert.match(css, /grid-template-columns:minmax\(0,1fr\) var\(--vq-sim-options-w\)!important/);
  assert.match(css, /@media\(max-width:1439px\)\{\.vq43-sim-layout\{grid-template-columns:minmax\(0,1fr\)!important/);
});

test('Agent status refreshes use the scroll-preserving renderer', () => {
  const block = js.slice(js.indexOf('async function checkSimulationAgent'), js.indexOf('function launchSimulationAgent'));
  assert.ok(block.includes('renderSimulationPreserveScroll()'));
  assert.ok(!block.includes('renderSimulation(); bindPageControls();'));
});

test('authoritative stylesheet has balanced braces', () => {
  const stripped = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const opens = [...stripped].filter((char) => char === '{').length;
  const closes = [...stripped].filter((char) => char === '}').length;
  assert.equal(opens, closes);
});
