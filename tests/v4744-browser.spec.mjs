import {test,expect} from '@playwright/test';
test('300 dates navigate in ten-date windows without filtering counts or moving the percentage axis',async({page})=>{
 await page.route('http://127.0.0.1:*/api/**',r=>r.fulfill({contentType:'application/json',body:'{"ok":false}'}));
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 const dates=await page.evaluate(()=>{
  const rows=Array.from({length:300},(_,i)=>({cellId:'CELL'+i,position:'AN(TOP)',captureTimestamp:new Date(Date.UTC(2025,0,1+i)).toISOString(),totalResult:i%2?'OK':'NG',tools:{}}));
  window.__VISIONQC_DEBUG__.seedRows(rows);return rows.map(r=>r.captureTimestamp.slice(0,10));
 });
 const section=page.locator('.vq43-main-history-dashboard'),points=section.locator('.vq43-history-point'),input=section.getByLabel('그래프 시작 날짜');
 const axis=await section.locator('.vq43-history-axis').boundingBox();
 await expect(points).toHaveCount(10);await expect(input).toHaveValue(dates[290]);
 await section.getByRole('button',{name:'이전 10개',exact:true}).click();await expect(input).toHaveValue(dates[280]);
 await input.fill(dates[0]);await input.dispatchEvent('change');await expect(points.first()).toHaveAttribute('data-vq-history-day',dates[0]);
 await expect(section.getByRole('button',{name:'이전 10개',exact:true})).toBeDisabled();
 await section.getByRole('button',{name:'다음 10개',exact:true}).click();await expect(input).toHaveValue(dates[10]);
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot())).total).toBe(300);
 const after=await section.locator('.vq43-history-axis').boundingBox();expect(after.x).toBe(axis.x);expect(after.width).toBe(axis.width);
 await points.first().click();expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot())).selected).toBe(dates[10]);await expect(input).toHaveValue(dates[10]);
 await section.getByRole('button',{name:'전체보기',exact:true}).click();
 await input.fill(dates[299]);await input.dispatchEvent('change');await expect(points).toHaveCount(10);await expect(input).toHaveValue(dates[290]);await expect(section.locator('.vq43-history-segment')).toHaveCount(9);
 await section.getByRole('button',{name:'최신',exact:true}).click();await expect(input).toHaveValue(dates[290]);await expect(points).toHaveCount(10);
 await expect(section.getByRole('button',{name:'다음 10개',exact:true})).toBeDisabled();
 await section.screenshot({path:'C:/Temp/vq4744-date-navigation.png'});
});
