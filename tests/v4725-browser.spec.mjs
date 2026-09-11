import {test,expect} from '@playwright/test';
test('strict date graph filters all main aggregates before Cell-Position deduplication and restores all',async({page})=>{
 await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 await page.evaluate(()=>{
  const row=(id,date,result)=>({cellId:id,position:'AN(TOP)',fullPath:'C:/20261120123456/TAB_'+id+'_'+date+'_CRACK.jpg',tools:{Crack:{tool:'Crack',result,score:0.9}},totalResult:result});
  window.__VISIONQC_DEBUG__.seedRows([row('CELL1','20260824080000','OK'),row('CELL1','20260903090000','NG'),row('CELL2','20260824090000','NG'),row('CELL3','20260903100000','OK')]);
 });
 const points=page.locator('[data-vq-action="dashboard-day"]');
 await expect(points).toHaveCount(2);
 let snap=await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot());
 expect(snap.total).toBe(3);expect(snap.days.map(d=>d.date)).toEqual(['2026-08-24','2026-09-03']);
 await points.first().click();
 snap=await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot());
 expect(snap.selected).toBe('2026-08-24');expect(snap.total).toBe(2);expect(snap.ng).toBe(1);
 await page.locator('[data-vq-action="dashboard-day"]').last().focus(); await page.keyboard.press('Enter');
 snap=await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot()); expect(snap.selected).toBe('2026-09-03');expect(snap.total).toBe(2);
 await page.locator('[data-vq-action="dashboard-all"]').click();
 snap=await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot());expect(snap.selected).toBe('');expect(snap.total).toBe(3);
});
test('timestamp format controls token activation and named rules save/load across reload',async({page})=>{
 await page.addInitScript(()=>localStorage.setItem('visionqc-v43-active-page','settings'));
 await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
 const mode=page.locator('[data-naming-field="dateTime"][data-naming-key="mode"]'), token=page.locator('[data-naming-field="dateTime"][data-naming-key="tokenIndex"]');
 await expect(mode).toHaveValue('compact'); await expect(token).toBeDisabled();
 await mode.selectOption('split'); await expect(token).toBeDisabled();
 await mode.selectOption('token');await expect(token).toBeEnabled();await token.fill('4');
 await page.locator('#vq43-naming-name').fill('token rule');
 await page.locator('[data-vq-action="naming-profile-save"]').click();
 await mode.selectOption('split');
 await page.locator('#vq43-naming-name').fill('split rule');
 await page.locator('[data-vq-action="naming-profile-save"]').click();
 await page.reload();
 await page.locator('#vq43-naming-saved').selectOption({label:'token rule · v1'});
 await page.locator('[data-vq-action="naming-profile-load"]').click();
 await expect(mode).toHaveValue('token');await expect(token).toHaveValue('4');await expect(token).toBeEnabled();
 await page.locator('#vq43-naming-saved').selectOption({label:'split rule · v1'});
 await page.locator('[data-vq-action="naming-profile-load"]').click();await expect(mode).toHaveValue('split');await expect(token).toBeDisabled();
});

test('CSV graph uses filename capture date instead of simulation execution date',async({page})=>{
 await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedCsv('Cell ID,Position,Total_result,Date,Time,FullPath,Crack_Result,Crack_Score\nJ1037G87P6119039,AN(TOP),NG,20260909,120000,C:/20261120123456/TAB_J1037G87P611903999_20260824074705_CRACK.jpg,NG,0.9\n'));
 const snap=await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot());
 expect(snap.total).toBe(1);expect(snap.days.map(d=>d.date)).toEqual(['2026-08-24']);
});
test('AI SUGGEST button calls local runtime without API key and keeps labels on failure',async({page})=>{
 const external=[]; let calls=0, fail=false;
 page.on('request',req=>{if(/generativelanguage|googleapis/.test(req.url()))external.push(req.url());});
 await page.route('**/api/classification/inspect-upload',async route=>{
   calls++; const body=route.request().postDataJSON();
   expect(body.imageBase64.length).toBeGreaterThan(10);expect(body.mode).toBe('green');
   await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(fail?{ok:false,error:'test inference failure'}:{ok:true,position:'CA(TOP)',record:{TotalResult:'NG',Judgement:'Crack',Tools:{Crack:{Tool:'Crack',Result:'NG',Score:.9}}}})});
 });
 await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedRuntimeToolColors());
 await page.getByRole('button',{name:'분류',exact:true}).click();
 await page.locator('input[type="file"][accept="image/*"]').setInputFiles({name:'CA(TOP)_test.svg',mimeType:'image/svg+xml',buffer:Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="red"/></svg>')});
 const button=page.getByRole('button',{name:/AI SUGGEST/i});
 await button.click();
 await expect.poll(()=>calls).toBe(1);
 await expect(page.locator('#vq43-toast')).toContainText('CRACK');
 fail=true; await button.click();await expect.poll(()=>calls).toBe(2);
 await expect(page.locator('#vq43-toast')).toContainText('실패');
 expect(external).toEqual([]);
});

test('history DB delete requires confirmation and refreshes the empty database',async({page})=>{
 let deleteCalls=0;
 await page.addInitScript(()=>localStorage.setItem('visionqc-v43-active-page','history'));
 await page.route('**/api/status',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({ok:true,agentVersion:'1.3.19',vpdlAvailable:true,running:false})}));
 await page.route('**/api/history/search',route=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({ok:true,totalCount:0,ngCount:0,uniqueCellCount:0,page:1,pageSize:50,daily:[],items:[],filterOptions:{positions:[],tools:[],workspaceTypes:[],workspaces:[]},databasePath:'C:/Temp/test.sqlite'})}));
 await page.route('**/api/history/delete',async route=>{deleteCalls++;expect(route.request().postDataJSON()).toEqual({confirm:'DELETE_ALL_HISTORY'});await route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({ok:true,deletedRuns:2,deletedImages:10,deletedToolResults:40,databasePath:'C:/Temp/test.sqlite'})});});
 await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
 const button=page.getByRole('button',{name:'DB 전체 삭제'});
 await expect(button).toBeVisible();
 page.once('dialog',dialog=>dialog.dismiss());await button.click();expect(deleteCalls).toBe(0);
 page.once('dialog',dialog=>dialog.accept());await button.click();await expect.poll(()=>deleteCalls).toBe(1);
 await expect(page.locator('#vq43-toast')).toContainText('10건을 삭제');
});

test('daily NG chart expands a five percent peak to a zero-to-eight-percent axis',async({page})=>{
 await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 await page.evaluate(()=>{
   const rows=Array.from({length:20},(_,index)=>({sourceFileName:'dynamic-axis.csv',sourceRowNumber:index+2,captureTimestamp:'2026-09-10T08:00:00',cellId:`AXIS${String(index).padStart(12,'0')}`,position:'AN(TOP)',totalResult:index===0?'NG':'OK',tools:{}}));
   window.__VISIONQC_DEBUG__.seedRows(rows);
 });
 const labels=await page.locator('.vq43-main-history-dashboard .vq43-history-line > text').allTextContents();
 expect(labels).toEqual(['0%','2%','4%','6%','8%']);
});
