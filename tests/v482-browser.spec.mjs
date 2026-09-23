import {test,expect} from '@playwright/test';
import fs from 'node:fs/promises';

async function start(page){
 await page.goto('/index.html?vqDebug=1&browserRegression=1');
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 await page.evaluate(()=>{
  window.showSaveFilePicker=undefined;
  const rows=[];
  for(const position of ['AN(TOP)','CA(TOP)'])for(let day=1;day<=2;day++)for(let cell=0;cell<20;cell++)for(let dup=0;dup<2;dup++){
   rows.push({cellId:`CELL${cell}`,position,captureTimestamp:`2026-02-0${day}T12:34:56`,totalResult:cell<17?'NG':'OK',sourceFileName:'input.csv',sourceRowNumber:rows.length+2,tools:{FoilDamage:{tool:'FoilDamage',result:cell<17?'NG':'OK',score:cell<10?.9:.7}}});
  }
  window.__VISIONQC_DEBUG__.seedRows(rows);
 });
}
async function csv(page,selector){
 const promise=page.waitForEvent('download');await page.locator(selector).click();
 const download=await promise;
 return (await fs.readFile(await download.path(),'utf8')).trim().split(/\r?\n/);
}
test('Tool exports exact dated NG Cells and respects threshold without duplicates',async({page})=>{
 await start(page);
 await page.locator('[data-vq-action="dashboard-day"][data-vq-history-day="2026-02-01"]').click();
 const selector='[data-vq-action="download-tool-ng"][data-position="AN(TOP)"]';
 const box=await page.locator(selector).boundingBox();const inputBox=await page.locator('.vq43-threshold-input[data-position="AN(TOP)"]').boundingBox();
 expect(box.x).toBeGreaterThan(inputBox.x+inputBox.width);
 await page.screenshot({path:'test-results/v482-dashboard.png',fullPage:true});
 let rows=await csv(page,selector);expect(rows).toHaveLength(18);
 expect(rows.slice(1).every(row=>row.includes('2026-02-01,,CELL')&&row.includes('AN(TOP)'))).toBeTruthy();
 const threshold=page.locator('.vq43-threshold-input[data-position="AN(TOP)"]');
 await threshold.fill('0.80');await threshold.dispatchEvent('change');
 rows=await csv(page,selector);expect(rows).toHaveLength(11);
 await page.locator('[data-vq-action="dashboard-all"]').click();
 rows=await csv(page,selector);expect(rows).toHaveLength(21);
});

test('Daily NG counts Cells once across Positions and dates with any selected NG',async({page})=>{
 await page.route('http://127.0.0.1:*/api/**',route=>route.abort());
 await start(page);
 const kpis=page.locator('.vq43-main-history-kpis');
 await expect(kpis.locator('b').nth(0)).toHaveText('40');await expect(kpis.locator('b').nth(1)).toHaveText('34');
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot())).days.map(d=>[d.total,d.ng])).toEqual([[20,17],[20,17]]);
 await page.locator('[data-vq-action="dashboard-day"][data-vq-history-day="2026-02-01"]').click();
 const threshold=page.locator('.vq43-threshold-input[data-position="AN(TOP)"]');
 await threshold.fill('0.80');await threshold.dispatchEvent('change');
 await expect(kpis.locator('b').nth(0)).toHaveText('20');await expect(kpis.locator('b').nth(1)).toHaveText('17');
 const report=await page.evaluate(()=>window.__VISIONQC_DEBUG__.buildReportHtml());
 expect(report).toContain('>85.0%</text>');expect(report).not.toContain('>67.5%</text>');
 await page.locator('[data-chart-position="CA(TOP)"]').uncheck();
 await expect(kpis.locator('b').nth(0)).toHaveText('20');await expect(kpis.locator('b').nth(1)).toHaveText('10');
 await expect(kpis.locator('b').nth(3)).toHaveText('50.00%');
 await page.locator('[data-chart-position="AN(TOP)"]').uncheck();
 await expect(kpis.locator('b').nth(0)).toHaveText('0');
});
test('Date graph Position selection and CSV include OK Cells and honor empty selection',async({page})=>{
 await start(page);
 await page.locator('[data-chart-position="CA(TOP)"]').uncheck();
 await expect(page.locator('.vq43-main-history-kpis').getByText('40',{exact:true})).toHaveCount(2);
 let rows=await csv(page,'[data-vq-action="download-chart-csv"][data-chart-scope="main"]');expect(rows).toHaveLength(41);
 expect(rows.slice(1).every(row=>row.includes('AN(TOP)'))).toBeTruthy();
 await page.locator('[data-vq-action="dashboard-day"][data-vq-history-day="2026-02-02"]').click();
 rows=await csv(page,'[data-vq-action="download-chart-csv"][data-chart-scope="main"]');expect(rows).toHaveLength(21);
 expect(rows.some(row=>row.includes(',OK,'))).toBeTruthy();
 await page.locator('[data-chart-position="AN(TOP)"]').uncheck();
 rows=await csv(page,'[data-vq-action="download-chart-csv"][data-chart-scope="main"]');expect(rows).toHaveLength(1);
 await expect(page.getByText('표시할 날짜별 이력이 없습니다.')).toBeVisible();
});
test('Output row limit has explicit readable foreground and background',async({page})=>{
 await start(page);await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('simulation'));
 const input=page.locator('[data-sim-field="csvMaxRows"]');await expect(input).toBeVisible();
 const style=await input.evaluate(el=>{const s=getComputedStyle(el);return {color:s.color,background:s.backgroundColor};});
 expect(style.color).toBe('rgb(237, 245, 255)');expect(style.background).toBe('rgb(7, 19, 33)');
 await input.screenshot({path:'test-results/v482-output-dark.png'});
 await page.evaluate(()=>document.body.classList.add('vq43-theme-light'));
 await input.screenshot({path:'test-results/v482-output-light.png'});
 const light=await input.evaluate(el=>{const s=getComputedStyle(el);return {color:s.color,background:s.backgroundColor};});
 expect(light.color).toMatch(/^rgb\(23, (32|43), (51|69)\)$/);expect(light.background).toBe('rgb(255, 255, 255)');
});
