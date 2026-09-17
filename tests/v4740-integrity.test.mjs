import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const js=readFileSync(new URL('../visionqc-extension.js',import.meta.url),'utf8');
const helpers=new Function(js.slice(js.indexOf('  function runtimeSignaturePath('),js.indexOf('  function clearSimulationRuntimeReadiness('))+';return {simulationRuntimeSignature,simulationGreenWorkspaceSignature,simulationRuntimeControlSignature,isCompatiblePreloadedRuntime};')();

test('content fingerprint signatures preserve browser readiness for unchanged and Integrated-to-Green configurations',()=>{
  const options={useGpu:true,gpuDevices:'0',detailedDiagnostics:false};
  const request={mode:'integrated',parallelPositions:true,autoDistributeGpu:true,green:options,blue:options,positions:[{key:'P1',greenWorkspacePath:'C:\\green.vrws',blueWorkspacePath:'C:\\blue.vrws'}]};
  const fingerprint=s=>s.replace(/\.VRWS/g,'.VRWS#'+'A'.repeat(64));
  const runtime={runtimePreloaded:true,runtimePreloadMode:'integrated',runtimePreloadSignature:fingerprint(helpers.simulationRuntimeSignature(request)),runtimePreloadControlSignature:helpers.simulationRuntimeControlSignature(request),runtimePreloadGreenWorkspaceSignature:fingerprint(helpers.simulationGreenWorkspaceSignature(request))};
  assert.equal(helpers.isCompatiblePreloadedRuntime(request,runtime),true);
  assert.equal(helpers.isCompatiblePreloadedRuntime({...request,mode:'green'},runtime),true);
  assert.equal(helpers.isCompatiblePreloadedRuntime({...request,mode:'green',green:{...options,gpuDevices:'1'}},runtime),false);
  assert.equal(helpers.isCompatiblePreloadedRuntime({...request,positions:[{...request.positions[0],greenWorkspacePath:'C:\\different.vrws'}]},runtime),false);
});

test('absolute paths with identical basenames are not collapsed into one image',()=>{
  const pathSource=js.slice(js.indexOf('  function csvFullPathValue('),js.indexOf('  function findFullPathColumn('));
  const source=js.slice(js.indexOf('  function viewerImagePathKey('),js.indexOf('  function scorePointMedia('));
  const merge=new Function(pathSource+source+';return mergeScoreViewerImages;')();
  assert.equal(merge([{fullPath:'C:\\first\\same.jpg'}],[{fullPath:'C:\\second\\same.jpg'}]).length,2);
  assert.equal(merge([{fullPath:'C:\\first\\same.jpg'}],[{fullPath:'c:/first/same.jpg'}]).length,1);
});
