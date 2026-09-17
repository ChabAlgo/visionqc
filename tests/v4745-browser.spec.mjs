import {test,expect} from '@playwright/test';
test('trailing dates fill ten slots and hover arrows advance five with clamped boundaries',async({page})=>{
 await page.route('http://127.0.0.1:*/api/**',r=>r.fulfill({contentType:'application/json',body:'{"ok":false}'}));
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 for(const count of [1,3,11,21,301]){
  const dates=await page.evaluate(count=>{const rows=Array.from({length:count},(_,i)=>({cellId:'C'+i,position:'AN(TOP)',captureTimestamp:new Date(Date.UTC(2025,0,1+i)).toISOString(),totalResult:'NG',tools:{}}));window.__VISIONQC_DEBUG__.seedRows(rows);return rows.map(r=>r.captureTimestamp.slice(0,10));},count);
  const section=page.locator('.vq43-main-history-dashboard'),chart=section.locator('.vq43-history-paged'),points=chart.locator('.vq43-history-point'),input=section.getByLabel('그래프 시작 날짜');
  await input.fill(dates[0]);await input.dispatchEvent('change');
  await expect(points).toHaveCount(Math.min(10,count));
  const prev=chart.getByRole('button',{name:'이전 5개 날짜',exact:true}),next=chart.getByRole('button',{name:'다음 5개 날짜',exact:true});
  await page.mouse.move(0,0);await expect(prev).toHaveCSS('opacity','0');
  await chart.hover();await expect(prev).toBeDisabled();
  if(count>10){
   await next.click();await expect(input).toHaveValue(dates[Math.min(5,count-10)]);
   await prev.click();await expect(input).toHaveValue(dates[0]);
   // Choose a start with exactly one date remaining after the next ten-date move.
   await input.fill(dates[count-11]);await input.dispatchEvent('change');
   await section.getByRole('button',{name:'다음 10개',exact:true}).click();
   await expect(input).toHaveValue(dates[count-10]);await expect(points).toHaveCount(10);
   await chart.hover();await expect(next).toBeDisabled();
   await expect(section.getByRole('button',{name:'다음 10개',exact:true})).toBeDisabled();
  }else await expect(next).toBeDisabled();
  expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot())).total).toBe(count);
  if(count===301)await section.screenshot({path:'C:/Temp/vq4745-chart-arrows.png'});
 }
});
