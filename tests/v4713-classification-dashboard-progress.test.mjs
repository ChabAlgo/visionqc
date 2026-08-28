import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, name), 'utf8');

test('classification restores focus only after the current image has loaded', () => {
  const base = read('assets/index-v4.4.33.js');
  assert.match(base, /VQ42_loadedImageId=Ee\.useRef\(null\)/);
  assert.match(base, /VQ42_restorePending=Ee\.useRef\(!1\)/);
  assert.match(base, /VQ42_restorePending\.current=!0,VQ42_loadedImageId\.current=null/);
  assert.match(base, /!VQ42_restorePending\.current&&VQ42_loadedImageId\.current===i\.id/);
  assert.match(base, /VQ42_restorePending\.current=!1/);
  assert.match(base, /VQ42_loadedImageId\.current!==null&&z\.width>0&&z\.height>0&&!oe\.current/);
  assert.match(base, /VQ42_loadedImageId\.current===i\.id&&z\.width>0&&z\.height>0/);
  assert.match(base, /VQ42_loadedImageId\.current!==i\.id\|\|requestAnimationFrame/);
  assert.match(base, /requestAnimationFrame\(\(\)=>\{if\(VQ42_loadedImageId\.current!==i\.id\)return/);
  assert.match(base, /VQ42_loadedImageId\.current=i\.id,P\(\{width:ge\.currentTarget\.naturalWidth/);
});

test('daily chart derives its viewBox width from the rendered dashboard geometry', () => {
  const js = read('visionqc-extension.js');
  assert.match(js, /renderedHeight = mainCompact \? 136 : 220/);
  assert.match(js, /availableWidth \* height \/ renderedHeight/);
  assert.match(js, /const left = 28, right = 6/);
});

test('main dashboard uses two Position columns and nests misses below Position NG', () => {
  const js = read('visionqc-extension.js');
  const css = read('visionqc-extension.css');
  assert.match(js, /<span>CSV 매칭<\/span>/);
  assert.match(js, /<span>정상 검출<\/span>/);
  assert.match(js, /const misses = \$\('\.vq43-main-misses', content\);[\s\S]*left\.append\(misses\)/);
  assert.match(css, /vq43-main-dashboard \.vq43-position-grid\{grid-template-columns:repeat\(2,minmax\(0,1fr\)\)!important/);
  assert.match(css, /vq43-threshold-input[^{]*\{[^}]*width:48px!important[^}]*font-size:9px!important/);
});

test('analysis upper area follows left filters and right condition-summary order', () => {
  const js = read('visionqc-extension.js');
  const css = read('visionqc-extension.css');
  assert.match(js, /vq43-analysis-upper-grid[\s\S]*vq43-analysis-left[\s\S]*vq43-filter[\s\S]*vq43-kpi-grid[\s\S]*vq43-analysis-right[\s\S]*vq43-analysis-export[\s\S]*vq43-actual-ng-minimum[\s\S]*vq43-summary-table/);
  assert.match(css, /vq43-analysis-upper-grid\{display:grid;grid-template-columns:minmax\(0,1fr\) minmax\(0,1fr\)/);
  assert.match(css, /vq43-analysis-page \.vq43-title\{font-size:25px!important/);
  assert.match(css, /body\.vq43-theme-light #vq43-shell \.vq43-analysis-page/);
});

test('running Simulation exposes a global progress rail on every page', () => {
  const js = read('visionqc-extension.js');
  const css = read('visionqc-extension.css');
  assert.match(js, /id="vq43-global-sim-progress"/);
  assert.match(js, /function updateGlobalSimulationProgress\(\)/);
  assert.match(js, /element\.hidden = !active/);
  assert.match(js, /updateGlobalSimulationProgress\(\);[\s\S]*if \(state\.page !== 'simulation'\) return/);
  assert.match(css, /#vq43-global-sim-progress\{position:fixed/);
});
