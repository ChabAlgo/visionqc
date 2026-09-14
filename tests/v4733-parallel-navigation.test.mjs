import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const web = readFileSync(new URL('../visionqc-extension.js', import.meta.url), 'utf8');
const server = readFileSync(new URL('../LocalAgent_v0.2.12/AgentServer.cs', import.meta.url), 'utf8');
const dto = readFileSync(new URL('../LocalAgent_v0.2.12/AgentDtos.cs', import.meta.url), 'utf8');
const green = readFileSync(new URL('../LocalAgent_v0.2.12/Engine/GreenOverlayProcessor.cs', import.meta.url), 'utf8');

test('Position simulation uses independent Agent processes with bounded parallel workers', () => {
  assert.match(dto, /bool parallelPositions/);
  assert.match(dto, /bool autoDistributeGpu/);
  assert.match(dto, /int maxParallelPositions/);
  assert.match(server, /Dictionary<string, PositionWorkerClient> _preloadedPositionWorkers/);
  assert.match(server, /StartPositionWorker/);
  assert.match(server, /Application\.ExecutablePath/);
  assert.match(server, /VISIONQC_AGENT_PARENT_PID/);
  assert.match(server, /MonitorParentProcess/);
  assert.match(server, /"\/api\/runtime\/preload"/);
  assert.match(server, /"\/api\/simulation\/start"/);
  assert.match(server, /ForwardParallelProgress/);
  assert.match(server, /RunParallelPositionSimulation/);
  assert.match(server, /SemaphoreSlim\(maxParallel, maxParallel\)/);
  assert.match(server, /RememberPreloadedPositionWorkersLocked/);
});

test('GPU assignment is automatic, round-robin, and included in Runtime compatibility signatures', () => {
  assert.match(server, /DetectGpuDeviceIndices/);
  assert.match(server, /nvidia-smi\.exe/);
  assert.match(server, /available\[index % available\.Count\]/);
  assert.match(server, /ParallelRuntimeSignature/);
  assert.doesNotMatch(server.match(/private static string ParallelRuntimeSignature[\s\S]*?\n        }/)?.[0] || '', /\|N:/);
  assert.doesNotMatch(web.match(/function simulationRuntimeSignature[\s\S]*?\n  }/)?.[0] || '', /\|N:/);
  assert.doesNotMatch(web.match(/function simulationRuntimeControlSignature[\s\S]*?\n  }/)?.[0] || '', /\|N:/);
  assert.match(web, /autoDistributeGpu:true/);
  assert.match(web, /maxParallelPositions:10/);
  assert.match(web, /positionGpuAssignments/);
});

test('parallel output files cannot collide and temporary integrated crops are deleted by the coordinator', () => {
  assert.match(green, /ResultFileSuffix/);
  assert.match(server, /parallelPositionKey = position\.key/);
  assert.match(server, /PositionCropRoot/);
  assert.match(server, /MergePositionResultCsv/);
  assert.match(server, /Directory\.Delete\(temporaryCropRoot, true\)/);
});

test('miss and history viewers keep ordered sequences and stop at both ends', () => {
  assert.match(web, /state\.modalSequenceKind = '미검'/);
  assert.match(web, /state\.modalSequenceKind = '검사 이력'/);
  assert.match(web, /function changeModalSequenceItem/);
  assert.match(web, /next < 0 \|\| next >= sequence\.length/);
  assert.match(web, /previousDisabled = .*sequenceIndex <= 0/);
  assert.match(web, /nextDisabled = .*sequenceIndex >= sequenceCount - 1/);
});
