import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(resolve(root, path), 'utf8');
const web = read('visionqc-extension.js');
const agent = read('LocalAgent_v0.2.12/AgentServer.cs');
const core = read('LocalAgent_v0.2.12/Engine/BlueCropCore.cs');
const lightCss = read('visionqc-v4433-clean.css');

test('v4.7.15 preserves Integrated Crop images for Viewer and makes light Simulation state identifiable', () => {
  assert.match(web, /const VERSION = '4\.7\.15'/);  assert.match(web, /keepCropImages:true/);
  assert.match(web, /viewerImageRetentionSchema/);
  assert.match(web, /결과 Crop 이미지 유지 \(Viewer용\)/);
  assert.match(agent, /_VisionQC_Integrated_Images/);
  assert.match(agent, /bool keepCropImages = integratedOptions\.keepCropImages/);
  assert.match(agent, /RunStreaming\(blue, green, keepCropImages, cropRoot/);
  assert.match(core, /if \(!keepCropImages\)/);
  assert.match(lightCss, /vq43-workspace-inspect\.loading/);
  assert.match(lightCss, /background:#eff9ff!important/);
  assert.match(lightCss, /vq43-sim-tabs button\.active\{background:#d9f1ff!important/);
  assert.match(lightCss, /box-shadow:inset 0 -4px 0 #0284c7!important/);
  assert.match(web, /function originalPathForViewer/);
  assert.match(web, /historyLookupPath/);
  assert.match(lightCss, /webkit-calendar-picker-indicator/);
  assert.match(lightCss, /text-slate-200.*truncate/);
});
