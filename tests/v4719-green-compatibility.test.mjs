import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read = p => readFileSync(new URL('../'+p, import.meta.url), 'utf8');
const server = read('LocalAgent_v0.2.12/AgentServer.cs');
const green = read('LocalAgent_v0.2.12/Engine/GreenOverlayProcessor.cs');
const web = read('visionqc-extension.js');
test('Green compatibility settings default off and reach the server separately',()=>{
  assert.match(web,/disableTensorRt:false, freshRuntime:false/);
  for(const name of ['disableTensorRt','freshRuntime']){
    assert.match(read('LocalAgent_v0.2.12/AgentDtos.cs'),new RegExp('public bool '+name));
    assert.ok(web.includes("simulationCheck('green','"+name+"'"));
  }
  assert.match(web,/state.simulationAgent.version !== EXPECTED_AGENT_VERSION/);
});
test('only standalone Green simulation changes policy and never caches the changed runtime',()=>{
  const run=server.slice(server.indexOf('private void RunSimulation'),server.indexOf('private void StartSimulationHistory'));
  assert.match(run,/runtimeReusable = !greenOptions.disableTensorRt && !greenOptions.freshRuntime/);
  assert.match(run,/oldControl.Dispose\(\);[\s\S]*?simulationControl = GreenRuntimeFactory.Create/);
  assert.match(run,/GreenOverlayProcessor.Run\(greenConfig, simulationControl, !greenOptions.freshRuntime/);
  assert.equal((server.match(/greenConfig.DisableTensorRt =/g)||[]).length,1);
  assert.match(run,/catch \(SysException ex\)[\s\S]*?CompleteSimulationHistory\("failed"/);
});
test('inference diagnostic survives native cleanup and does not export data',()=>{
  assert.match(green,/last-green-inference.txt[\s\S]*?AgentDiagnostics.Measure\("Sample.Process"[\s\S]*?sample.Process\(tool\)/);
  assert.match(green,/last-green-failure.txt[\s\S]*?CaptureEnvironment\("inference-failure"\)[\s\S]*?throw new System.InvalidOperationException/);
  assert.match(read('LocalAgent_v0.2.12/Services/DiagnosticTrace.cs'),/CaptureEnvironment[\s\S]*?WriteLoadedLibraries\(\)/);
  const policy=read('LocalAgent_v0.2.12/Services/GreenRuntimePolicy.cs');
  assert.match(policy,/ProcessWithTrt/);
  assert.match(policy,/property.GetValue\(parameters, null\), disabled/);
  assert.doesNotMatch(policy,/\.Save\(|OptimizeTensorRT|HttpClient|WebRequest/);
});
