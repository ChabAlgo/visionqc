import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const web=read('visionqc-extension.js'),server=read('LocalAgent_v0.2.12/AgentServer.cs');
const factory=read('LocalAgent_v0.2.12/Services/GreenRuntimeFactory.cs');
const trace=read('LocalAgent_v0.2.12/Services/DiagnosticTrace.cs');
const helpers=new Function(web.slice(web.indexOf('  function runtimeSignaturePath('),web.indexOf('  function simulationGreenWorkspaceSignature('))+';return {simulationRuntimeSignature,simulationRuntimeControlSignature};')();
test('production diagnostics default quiet and experimental controls are removed',()=>{
 assert.match(web,/detailedDiagnostics:false, disableOptimizedGpuMemory:false/);
 for(const name of ['detailedDiagnostics','disableOptimizedGpuMemory']){
  assert.ok(!web.includes("simulationCheck('green','"+name+"'"));
  assert.ok(read('LocalAgent_v0.2.12/AgentDtos.cs').includes('bool '+name));
 }
 assert.match(factory,/new LocalRuntime.Control\(new LocalRuntime.LibraryAccess\(\), mode, devices, true\)/);
 assert.match(factory,/if \(disableMemoryPool\)[\s\S]*?control.OptimizedGPUMemory\(0\)/);
 assert.doesNotMatch(factory,/GpuMode.Deferred|InitializeComputeDevices|\.Save\(/);
});
test('runtime signatures separate logging and memory policy but preserve unmodified integrated reuse',()=>{
 const req={mode:'green',green:{useGpu:true,gpuDevices:'0',detailedDiagnostics:false},blue:{useGpu:true,gpuDevices:'0'},positions:[]};
 const baseline=helpers.simulationRuntimeSignature(req);
 assert.notEqual(baseline,helpers.simulationRuntimeSignature({...req,green:{...req.green,detailedDiagnostics:true}}));
 assert.notEqual(baseline,helpers.simulationRuntimeSignature({...req,green:{...req.green,disableOptimizedGpuMemory:true}}));
 assert.equal(helpers.simulationRuntimeControlSignature(req),helpers.simulationRuntimeControlSignature({...req,mode:'integrated'}));
 assert.match(server,/sb.Append\(RuntimeDiagnosticSignature\(req\)\)/);
 assert.match(server,/options.disableOptimizedGpuMemory/);
});
test('failure trace is local, read-only, bounded and retains exception detail',()=>{
 assert.match(trace,/last-sdk-failure.txt/);assert.match(trace,/HResult=0x/);
 for(const stage of ['Runtime.Create','Workspace.Load','Sample.AddImage','Sample.Process','Result.ReadMarking','Result.ReadHeatmap']){
  assert.ok([factory,server,read('LocalAgent_v0.2.12/Engine/GreenOverlayProcessor.cs')].join('\n').includes('"'+stage+'"'));
 }
 assert.match(trace,/driver_model.current,compute_mode,memory.total,memory.used,memory.free/);
 assert.match(trace,/--query-compute-apps=gpu_uuid,pid,used_gpu_memory/);
 assert.match(trace,/WaitForExit\(3000\)/);
 assert.doesNotMatch(trace,/HttpClient|WebRequest|--driver-model|--gpu-reset|--compute-mode/);
});
