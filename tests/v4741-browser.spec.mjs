import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';

async function open(page) {
  await page.route('http://127.0.0.1:*/api/**', route => route.fulfill({contentType:'application/json',body:'{"ok":false}'}));
  await page.goto('/index.html?vqDebug=1&browserRegression=1');
  await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
}

test('label export preserves text Cell IDs, deduplicates each label, makes valid unique sheet names',async({page})=>{
  await open(page);
  const result=await page.evaluate(async()=>{
    const d=window.__VISIONQC_DEBUG__;
    const items=[{fileName:'20260202_104601_P163GG22M210013608_TN1006.jpg',labels:['A','B']},
      {fileName:'20260202_104602_P163GG22M210013608_TN1006.jpg',labels:['A']},
      {fileName:'20260202_104603_P163GG22M210013717_TN1007.jpg',labels:[]}];
    const sheets=d.classificationCellSheets(items,[{id:'A',label:'Crack/NG'},{id:'B',label:'Crack:NG'}]);
    const zip=await JSZip.loadAsync(await (await d.buildClassificationWorkbook(sheets)).arrayBuffer());
    const xml=await zip.file('xl/worksheets/sheet1.xml').async('text');
    let missing='';try {d.classificationCellSheets([{fileName:'unknown.jpg',labels:['A']}],[]);}catch(e){missing=e.message;}
    return {sheets,xml,missing,workbook:await zip.file('xl/workbook.xml').async('text')};
  });
  expect(result.sheets).toEqual([{name:'Crack_NG',ids:['P163GG22M2100136']},{name:'Crack_NG_1',ids:['P163GG22M2100136']}]);
  expect(result.xml).toContain('t="inlineStr"');
  expect(result.xml).not.toContain('<f>');
  expect(result.missing).toContain('내보내기를 중단');
  expect(result.workbook).toContain('sheetId="2"');
});

test('classification header exports the labels currently assigned in the image UI',async({page},testInfo)=>{
  await open(page);
  const name='20260202_104601_P163GG22M210013608_TN1006.svg';
  await page.locator('input[type="file"][accept="image/*"]').setInputFiles({name,mimeType:'image/svg+xml',buffer:Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100"><rect width="200" height="100" fill="red"/></svg>')});
  await expect(page.locator(`main img[alt="${name}"]`).first()).toBeVisible();
  await page.keyboard.press('o');
  await expect(page.locator('#vq43-export-label-cells')).toBeVisible();
  const download=page.waitForEvent('download');
  await page.locator('#vq43-export-label-cells').click();
  const file=await download;
  await file.saveAs(testInfo.outputPath('classified-cell-ids.xlsx'));
  await page.screenshot({path:testInfo.outputPath('classification-header.png')});
  expect(file.suggestedFilename()).toMatch(/\.xlsx$/);
  const bytes=Array.from(await readFile(await file.path()));
  const contents=await page.evaluate(async bytes=>{
    const zip=await JSZip.loadAsync(new Uint8Array(bytes));
    return {workbook:await zip.file('xl/workbook.xml').async('text'),sheet:await zip.file('xl/worksheets/sheet1.xml').async('text')};
  },bytes);
  expect(contents.workbook).toContain('name="OK"');
  expect(contents.sheet).toContain('P163GG22M2100136');
});

test('completion prompt requires explicit choice, retries failures and sends the exact run ID',async({page})=>{
  await open(page); let attempts=[];
  await page.route('**/api/simulation/history-decision',async route=>{
    attempts.push(route.request().postDataJSON());
    await route.fulfill({contentType:'application/json',body:JSON.stringify(attempts.length===1?{ok:false,error:'Retry test'}:{ok:true,historyDecision:'saved'})});
  });
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.offerSimulationHistorySave({running:false,historyDecision:'pending',simulationRunId:'run-test'}));
  expect(attempts).toHaveLength(0);
  await page.getByRole('button',{name:'Yes · 저장'}).click();
  await expect(page.locator('#vq43-history-save-prompt')).toContainText('Retry test');
  await page.getByRole('button',{name:'Yes · 저장'}).click();
  await expect(page.locator('#vq43-history-save-prompt')).toHaveCount(0);
  expect(attempts).toEqual([{runId:'run-test',decision:'save'},{runId:'run-test',decision:'save'}]);
});

test('No declines saving, and merely loading a runtime does not show the prompt',async({page})=>{
  await open(page); const decisions=[];
  await page.route('**/api/simulation/history-decision',async route=>{
    decisions.push(route.request().postDataJSON());await route.fulfill({contentType:'application/json',body:'{"ok":true,"historyDecision":"declined"}'});
  });
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.offerSimulationHistorySave({running:false,simulationRunId:'runtime-only'}));
  await expect(page.locator('#vq43-history-save-prompt')).toHaveCount(0);
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.offerSimulationHistorySave({running:false,historyDecision:'pending',simulationRunId:'run-no'}));
  await page.getByRole('button',{name:'No · 저장 안 함'}).click();
  await expect(page.locator('#vq43-history-save-prompt')).toHaveCount(0);
  expect(decisions).toEqual([{runId:'run-no',decision:'discard'}]);
});

test('Position changes refresh dependent lists and clicking date input invokes calendar',async({page},testInfo)=>{
  await open(page);
  await page.route('**/api/history/search',async route=>{
    const f=route.request().postDataJSON();
    await route.fulfill({contentType:'application/json',body:JSON.stringify({ok:true,totalCount:1,page:1,items:[{imageId:1,cellId:'CELL',position:f.position,inspectedAtUtc:'2026-09-16T08:34:26.5892578Z',tools:[]}],daily:[],filterOptions:{positions:['AN(TOP)','CA(TOP)'],tools:f.position==='AN(TOP)'?['Crack']:['Edge'],workspaceTypes:['green'],workspaces:[{value:f.position,label:f.position+' Workspace',workspaceType:'green'}]}})});
  });
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedHistoryFilters());
  await page.locator('[data-history-field="position"]').selectOption('AN(TOP)');
  await expect(page.locator('[data-history-field="tool"] option')).toHaveText(['전체','Crack']);
  await expect(page.locator('[data-history-field="workspaceKey"] option')).toHaveText(['전체','AN(TOP) Workspace']);
  await expect(page.locator('.vq43-history-table')).toContainText('2026-09-16 / 08:34:26');
  await page.screenshot({path:testInfo.outputPath('history-filters.png')});
  await page.evaluate(()=>{window.pickerCount=0;HTMLInputElement.prototype.showPicker=function(){window.pickerCount++;};});
  await page.locator('[data-history-field="fromDate"]').click({position:{x:20,y:15}});
  expect(await page.evaluate(()=>window.pickerCount)).toBe(1);
});
