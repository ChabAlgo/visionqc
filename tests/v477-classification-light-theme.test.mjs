import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const css = readFileSync(resolve(root, 'visionqc-v4433-clean.css'), 'utf8');

test('v4.7.7 keeps classification controls compact and makes Workspace states readable in light mode', () => {
  assert.match(css, /vq43-host-header \.vq43-stats\{order:-1!important/);
  assert.match(css, /vq43-stats>\.flex\.flex-col\.shrink-0\{padding:7px 9px!important/);
  assert.match(css, /vq43-workspace-inspect>span,.vq43-workspace-tool\)\{background:#f8fcff!important/);
  assert.match(css, /vq43-workspace-card\.loading,.vq43-workspace-card\.pending,.vq43-workspace-card\.ok\)\{background:#f7fcff!important/);
  assert.match(css, /vq43-sim-option-field>span[^\n]*font-size:13px!important/);
  assert.match(css, /absolute\.bottom-8\.z-40\.pointer-events-none\{left:20px!important;bottom:16px!important/);
  assert.match(css, /-webkit-line-clamp:2!important/);
  assert.match(css, /text-slate-[^\n]*text-gray-[^\n]*text-zinc-[^\n]*text-white/);
});
