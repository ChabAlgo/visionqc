import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { setTimeout as sleep } from 'node:timers/promises';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const base = 'http://127.0.0.1:17891';
try {
  await fetch(base + '/api/status', { signal:AbortSignal.timeout(5000) });
  throw new Error('An Agent is already running; stop it before this isolated test');
} catch (error) { if (String(error).includes('already running') || error.name === 'TimeoutError') throw error; }
const stage = mkdtempSync(join(tmpdir(), 'VisionQC-core-smoke-'));
const noVpdl = join(stage, 'NoVpdl'); mkdirSync(noVpdl);
cpSync(join(repo, 'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/Core'), join(stage, 'Core'), { recursive:true });
const web = join(stage, 'Web'); mkdirSync(web);
for (const path of ['index.html','visionqc-extension.js','visionqc-extension.css','visionqc-v470.css','visionqc-v4433-clean.css','assets']) cpSync(join(repo, path), join(web, path), { recursive:true });
const child = spawn(join(stage, 'Core/VisionQC.CoreWorker.exe'), [], {
  cwd:stage, windowsHide:true, stdio:'ignore',
  env:{ ...process.env, COGNEX_VPDL_ROOT:noVpdl, VISIONQC_AGENT_HOME:stage, VISIONQC_HISTORY_DB_PATH:join(stage,'history.sqlite') }
});
try {
  let status;
  for (let index=0; index<30; index++) {
    if (child.exitCode !== null) throw new Error('Core Worker exited: ' + child.exitCode);
    try { status=await (await fetch(base+'/api/status',{signal:AbortSignal.timeout(5000)})).json(); break; } catch { await sleep(500); }
  }
  assert.equal(status?.vpdlAvailable, false);
  assert.equal(status.agentVersion, JSON.parse(readFileSync(join(repo,'RELEASE_MANIFEST.json'),'utf8')).agentVersion);
  const page = await (await fetch(base+'/')).text(); assert.match(page, /VisionQC/);
  const response = await fetch(base+'/api/simulation/start', {method:'POST',headers:{'content-type':'application/json'},body:'{}'});
  assert.equal((await response.json()).ok, false);
  console.log(JSON.stringify({passed:true,stage,agentVersion:status.agentVersion,offlinePage:true,simulationRejectedWithoutVpdl:true}));
} finally {
  try { await fetch(base+'/api/agent/exit',{method:'POST',headers:{'content-type':'application/json'},body:'{}',signal:AbortSignal.timeout(3000)}); } catch {}
  await sleep(2000); if(child.exitCode===null) child.kill();
}
