import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../'+path,import.meta.url),'utf8');
const agent='LocalAgent_v0.2.12/';
test('original Green runs independently while legacy and integrated modes remain available',()=>{
  const server=read(agent+'AgentServer.cs'), ui=read('visionqc-extension.js');
  assert.match(server,/if \(greenOptions.originalProcess\)/);
  assert.match(server,/previewControl.Dispose\(\);[\s\S]*?GreenProcessHost.Run/);
  assert.match(server,/GreenOverlayProcessor.Run\(greenConfig, simulationControl, !greenOptions.freshRuntime/);
  assert.match(ui,/originalProcess:true/);
  assert.doesNotMatch(ui,/state.simulationConfig/);
  assert.match(ui,/field === 'originalProcess'\) refreshSimulationOptionsOnly/);
});
test('runner owns its Runtime and preserves stored Workspace settings',()=>{
  const runner=read(agent+'GreenRunner/Program.cs'), factory=read(agent+'Services/GreenRuntimeFactory.cs');
  const original=factory.slice(factory.indexOf('CreateOriginal'),factory.indexOf('internal static LocalRuntime.Control Create('));
  assert.match(original,/new LocalRuntime.Control\(mode, devices\)/);
  assert.doesNotMatch(original,/OptimizedGPUMemory|CaptureEnvironment|LibraryAccess/);
  assert.match(runner,/Task.Run\(\(\) => GreenOverlayProcessor.Run/);
  assert.match(runner,/config.DisableTensorRt = false/);
  assert.match(runner,/config.DisableOptimizedGpuMemory = false/);
  assert.match(runner,/_installation.NativeDirectory/);
  assert.doesNotMatch(runner,/Workspace\.Save|\.Optimize\(|nvidia-smi/);
});
test('process pipe separates diagnostics and preserves native failure/cancel boundaries',()=>{
  const host=read(agent+'Services/GreenProcessHost.cs');
  assert.match(host,/WindowsIdentity.GetCurrent\(\).User/);
  assert.match(host,/PipeAccessRights.FullControl/);
  assert.match(host,/RedirectStandardOutput = true/);
  assert.match(host,/GREEN_CHILD_STDOUT/);
  assert.match(host,/cancelling.ElapsedMilliseconds > 15000/);
  assert.match(host,/PipeCommandWriter/);
  assert.match(host,/Message after terminal Green result/);
  assert.doesNotMatch(host,/GetProcessesByName|taskkill|Process\.GetProcesses\(/);
});
test('all shipped SDK workers include matching standalone runner',()=>{
  const build=read(agent+'BUILD_VPDL_WORKERS.ps1'),project=read(agent+'GreenRunner/VisionQC.GreenRunner.csproj');
  assert.match(build,/GreenRunner\\VisionQC.GreenRunner.csproj/);
  assert.match(build,/VisionQC.GreenRunner.exe/);
  assert.match(project,/\.\.\\Engine\\GreenOverlayProcessor.cs/);
  assert.doesNotMatch(project,/AgentServer.cs|System.Data.SQLite/);
});
test('pipe result includes filter counts, per-tool counts and per-position CSV paths',()=>{
  const source=read(agent+'Services/GreenProcessProtocol.cs');
  for(const key of ['FilterCellIdCount','SkippedByCellIdCount','NgCountByTool','CountByJudgement','SlotCsvPaths','ElapsedTicks'])
    assert.ok(source.includes(key),key);
  assert.match(source,/StringComparer.OrdinalIgnoreCase/);
});
