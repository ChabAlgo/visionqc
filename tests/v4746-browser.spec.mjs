import {test,expect} from '@playwright/test';
import {readFile} from 'node:fs/promises';

test('report includes every date in rows of twenty and changes nothing outside charts',async({page,context})=>{
 await page.route('http://127.0.0.1:*/api/**',route=>route.abort());
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 const render=async(target,count)=>target.evaluate(count=>{
  const rows=Array.from({length:count},(_,i)=>({cellId:'CELL'+i,position:'AN(TOP)',captureTimestamp:new Date(Date.UTC(2026,0,1+i)).toISOString(),sourceFileName:'Full_dates.csv',workspaceName:'Production.vrws',totalResult:i%3?'NG':'OK',tools:{Crack:{tool:'Crack',result:i%3?'NG':'OK',score:.9}}}));
  window.__VISIONQC_DEBUG__.seedRows(rows);return window.__VISIONQC_DEBUG__.buildReportHtml();
 },count);
 const report=await context.newPage();
 for(const count of [0,1,20,21,40,41,300]){
  const html=await render(page,count);await report.setContent(html);
  await expect(report.locator('.chart')).toHaveCount(Math.ceil(count/20));
  await expect(report.locator('.chart circle')).toHaveCount(count);
  const sizes=await report.locator('.chart').evaluateAll(charts=>charts.map(c=>c.querySelectorAll('circle').length));
  expect(sizes.every(size=>size>0&&size<=20)).toBe(true);
  const dates=await report.locator('.chart circle').evaluateAll(nodes=>nodes.map(n=>n.dataset.date));
  expect(new Set(dates).size).toBe(count);expect(dates).toEqual([...dates].sort());
  if(count===41){
   // Captured from v4.8.7 (add35a6), removing only .chart sections.
   const outside=await readFile(new URL('./fixtures/report-layout-v486.html',import.meta.url),'utf8');
   expect(await report.evaluate(()=>{const doc=document.documentElement.cloneNode(true);doc.querySelectorAll('.chart').forEach(n=>n.remove());return doc.outerHTML;})).toBe(outside);
   await report.pdf({path:'C:/Temp/vq487-41-days.pdf',preferCSSPageSize:true,printBackground:true});
  }
 }
 await report.close();
});
test('report uses captured CSV and workspace, readable thresholds and plain selectable missed IDs',async({page,context})=>{
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 const html=await page.evaluate(()=>{window.__VISIONQC_DEBUG__.seedReportMetadata();return window.__VISIONQC_DEBUG__.buildReportHtml();});
 const report=await context.newPage();await report.setContent(html);
 await expect(report.locator('.metadata').first()).toContainText('Inspection_20260918.csv');
 await expect(report.locator('.metadata').first()).toContainText('Quality_Workspace.vrws');
 await expect(report.locator('body')).not.toContainText('LIVE Simulation');await expect(report.locator('body')).not.toContainText('Wrong.csv');
 await expect(report.locator('.threshold').first()).toHaveText('0.6500');
 const ids=await report.locator('.miss-list pre').allTextContents();expect(ids).toHaveLength(4);for(const list of ids){expect(list.split('\n')).toHaveLength(100);expect(list).not.toMatch(/^\d{2} /m);}
 await report.evaluate(()=>Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:async text=>window.copiedIds=text}}));
 await report.locator('.copy').first().click();expect(await report.evaluate(()=>window.copiedIds)).toBe(ids[0]);
 await expect(report.locator('.miss-mix')).toBeVisible();await expect(report.locator('.chart svg')).toBeVisible();
 expect(await report.locator('table').first().evaluate(el=>parseFloat(getComputedStyle(el).fontSize))).toBeGreaterThanOrEqual(13);
 await report.pdf({path:'C:/Temp/vq4746-with-misses.pdf',preferCSSPageSize:true,printBackground:true});
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedRows([{cellId:'ZERO',position:'AN(TOP)',sourceFileName:'Saved_Log.csv',captureTimestamp:'2026-09-18T08:00:00',workspaceName:'Production.vrws',totalResult:'NG',tools:{Crack:{tool:'Crack',result:'NG',score:.9}}}]));
 await report.setContent(await page.evaluate(()=>window.__VISIONQC_DEBUG__.buildReportHtml()));
 await expect(report.locator('.miss-mix')).toHaveCount(0);await expect(report.locator('.miss-list')).toHaveCount(0);await expect(report.locator('.metadata').first()).toContainText('Saved_Log.csv');await expect(report.locator('.metadata').first()).toContainText('Production.vrws');
 await report.pdf({path:'C:/Temp/vq4746-no-misses.pdf',preferCSSPageSize:true,printBackground:true});
 await report.close();
});

test('approved report groups position charts before dates and keeps workspace mapping',async({page,context})=>{
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 const html=await page.evaluate(()=>{window.__VISIONQC_DEBUG__.seedApprovedReport();return window.__VISIONQC_DEBUG__.buildReportHtml();});
 const report=await context.newPage();await report.setContent(html);
 await expect(report.locator('h1')).toHaveCount(1);await expect(report.locator('.metadata')).toHaveCount(1);
 await expect(report.locator('.position-results .position-bars')).toBeVisible();await expect(report.locator('.position-results .miss-mix')).toBeVisible();
 expect(await report.locator('.position-results').evaluate(el=>el.nextElementSibling.classList.contains('chart'))).toBe(true);
 const rows=await report.locator('.metadata').first().locator('tbody tr').allTextContents();expect(rows.join(' ')).toContain('CA_TOP__Model.vrws');expect(rows.join(' ')).toContain('AN_BOT__Model.vrws');
 await expect(report.locator('.miss-list pre')).toHaveCount(3);await expect(report.locator('.tool-ratio')).toHaveCount(15);
 await report.pdf({path:'C:/Temp/vq4747-approved.pdf',preferCSSPageSize:true,printBackground:true});await report.close();
});
