import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const js = readFileSync(resolve(root, 'visionqc-extension.js'), 'utf8');

test('CSV FullPath is retained and used before manually selected Actual NG images', () => {
  assert.match(js, /function csvFullPathValue/);
  assert.match(js, /function findFullPathColumn/);
  assert.match(js, /fullPath: fullPathIndex >= 0/);
  assert.match(js, /function csvImagesForRecord/);
  assert.match(js, /CSV FullPath Image/);
  assert.match(js, /\/api\/image\/preview/);
  const start = js.indexOf('function csvFullPathValue');
  const end = js.indexOf('function findFullPathColumn', start);
  const csvFullPathValue = new Function(`${js.slice(start, end)}; return csvFullPathValue;`)();
  assert.equal(csvFullPathValue('C:\\Images\\sample.jpg'), 'C:\\Images\\sample.jpg');
  assert.equal(csvFullPathValue('\\\\server\\share\\sample.png'), '\\\\server\\share\\sample.png');
  assert.equal(csvFullPathValue('relative/sample.jpg'), '');
});

test('CSV analysis history is explicit, chunked, and uses the active naming profile', () => {
  const save = js.slice(js.indexOf('async function saveCsvAnalysisHistory'), js.indexOf('async function chooseNgFolder'));
  assert.match(save, /\/api\/history\/import/);
  assert.match(save, /batch\.length >= 200/);
  assert.match(save, /namingProfile:state\.namingProfile/);
  assert.match(save, /원본 이미지는 복사하지 않고/);
  assert.match(js, /data-vq-action="save-csv-history"/);
});

test('Heatmap overlay save controls are Green-only, including Integrated mode', () => {
  const filter = js.slice(js.indexOf('function greenFilterOptions'), js.indexOf('function greenRuntimeOptions'));
  const runtime = js.slice(js.indexOf('function greenRuntimeOptions'), js.indexOf('function detectedGreenToolNames'));
  assert.doesNotMatch(filter, /integrated','heatmapImageSave/);
  assert.match(runtime, /function greenRuntimeOptions\(integrated=false\)/);
  assert.match(runtime, /NG 원본 위 Heatmap Overlay 저장/);
  assert.match(runtime, /!integrated/);
});

test('Actual NG folders defer original-file reads and exclude high-score other-Tool NG rows from every detected-NG score view', () => {
  const scan = js.slice(js.indexOf('async function scanNgDirectory'), js.indexOf('async function chooseNgPositionFolder'));
  const analysis = js.slice(js.indexOf('function scorePoints'), js.indexOf('function downloadScoreFilterCsv'));
  assert.match(scan, /function actualNgTargetKeys/);
  assert.match(scan, /fileHandle:handle/);
  assert.doesNotMatch(scan, /await handle\.getFile\(\)/);
  assert.match(analysis, /function actualNgMinimumScore/);
  assert.match(analysis, /otherToolNgScores/);
  assert.match(js, /function recordOtherToolNgScores/);
  assert.match(js, /function analysisScorePointOptions/); assert.match(js, /function actualNgScoreCandidates/);
  const mainStart = js.indexOf('const positionToolSummaries');
  const mainEnd = js.indexOf('const actualKeys = Array.from', mainStart);
  const mainSummaries = js.slice(mainStart, mainEnd);
  assert.match(mainSummaries, /const actualNgScores = actualNgScoreCandidates\(actualMatchedRecords, tool, state\.actualNgOtherToolExclusionScore\)/);
  assert.match(mainSummaries, /minNgScore: actualNgScores\.eligible\.length \? Math\.min/);
  assert.match(js, /excludeOtherToolNg:true/);
  assert.match(js, /id="vq43-actual-ng-exclusion-score"/);

  const start = js.indexOf('function actualNgDetectedExclusion');
  const end = js.indexOf('function downloadScoreFilterCsv', start);
  const state = {
    actualNgOtherToolExclusionScore: 0.80,
    model: {
      actualMap: new Map([['AN(TOP)|CELL-1', [{}]]]),
      records: [{
        key:'AN(TOP)|CELL-1', cellId:'CELL-1', position:'AN(TOP)',
        sourceRows:[
          { totalResult:'NG', tools:{ Crack:{ result:'NG', score:0.7495 } } },
          { totalResult:'NG', tools:{ Welding:{ result:'NG', score:0.5071 } } }
        ]
      }]
    }
  };
  const helpers = new Function('state', `const clampScore=(value,fallback=0.50)=>{const parsed=Number(value);return Number.isFinite(parsed)?Math.max(0.50,Math.min(1.00,parsed)):fallback;};${js.slice(start, end)};return {scorePoints,analysisScorePointOptions,actualNgMinimumScore,actualNgScoreCandidates};`)(state);
  assert.equal(helpers.scorePoints('Crack', 'ACTUAL_NG_TOOL_NG', 'ALL').length, 1);
  assert.equal(helpers.scorePoints('Crack', 'ACTUAL_NG_TOOL_NG', 'ALL', helpers.analysisScorePointOptions('ACTUAL_NG_TOOL_NG')).length, 1);
  assert.equal(helpers.actualNgMinimumScore('Crack', 'ALL', 0.80).eligible.length, 1);
  state.actualNgOtherToolExclusionScore = 0.70;
  assert.deepEqual(helpers.actualNgScoreCandidates(state.model.records, 'Welding', 0.70), { eligible:[], excluded:[0.5071], threshold:0.70 });
  assert.equal(helpers.scorePoints('Welding', 'ACTUAL_NG_TOOL_NG', 'ALL', helpers.analysisScorePointOptions('ACTUAL_NG_TOOL_NG')).length, 0);
  assert.equal(helpers.actualNgMinimumScore('Welding', 'ALL', 0.70).min, null);
  state.actualNgOtherToolExclusionScore = 0.80;
  assert.equal(helpers.scorePoints('Welding', 'ACTUAL_NG_TOOL_NG', 'ALL', helpers.analysisScorePointOptions('ACTUAL_NG_TOOL_NG')).length, 1);
});

test('main date dashboard stays in the current analysis set and Auto Scroll reacts only to new log lines', () => {
  const dashboardStart = js.indexOf('function dashboardDateFromText');
  const dashboardEnd = js.indexOf('function mainHistoryDashboardPanel', dashboardStart);
  const state = { model:{ records:[
    { cellId:'CELL-1', position:'AN(TOP)', totalResult:'NG', sourceRows:[{ fullPath:'C:/images/20260203_085900_CELL-1.jpg' }] },
    { cellId:'CELL-2', position:'CA(TOP)', totalResult:'OK', sourceRows:[{ fullPath:'C:/images/20260203_085901_CELL-2.jpg' }] }
  ] } };
  const currentAnalysisDashboardData = new Function('state', `${js.slice(dashboardStart, dashboardEnd)};return currentAnalysisDashboardData;`)(state);
  assert.deepEqual(currentAnalysisDashboardData(), { totalCount:2, ngCount:1, uniqueCellCount:2, daily:[{ date:'2026-02-03', total:2, ng:1, ngRate:0.5 }] });
  const statusDom = js.slice(js.indexOf('function updateSimulationStatusDom'), js.indexOf('function simulationModeLabel'));
  const appendLog = js.slice(js.indexOf('function appendSimulationLog'), js.indexOf('function simulationLogLineHtml'));
  assert.doesNotMatch(statusDom, /scrollSimulationLogToBottom/);
  assert.match(appendLog, /scrollSimulationLogToBottom/);
});
