import {test,expect} from '@playwright/test';
import {readFileSync} from 'node:fs';
const manifest=JSON.parse(readFileSync(new URL('../RELEASE_MANIFEST.json',import.meta.url)));
const id='0123456789abcdef0123456789abcdef';
const day={date:'2026-02-03',total:121,ng:121,ngRate:1};
const model={remote:true,dailyUnit:'cell',recordCount:121,rawRowCount:121,uniqueCellCount:121,ngCellCount:121,ngCellRate:1,missCount:0,actualUniqueCount:0,matchedActualCount:0,unmatchedActualCount:0,duplicateCount:0,totalNg:121,unknownRows:0,tools:['Crack'],daily:[day],dateRange:{firstDate:day.date,lastDate:day.date,dateCount:1},workspaces:[],positionSummaries:[{position:'AN(TOP)',input:true,total:121,rawRows:121,ng:121,ok:0,ngRate:1,actualNg:0,detected:0,misses:0,unmatched:0}],positionToolSummaries:[{position:'AN(TOP)',input:true,totalNg:121,tools:[{tool:'Crack',ng:121,denominator:121,rate:1,threshold:.8,minNgScore:null}]}]};
async function setup(page,remote=true){
 const requests=[];let settings={automatic:true,retentionDays:30,maxMegabytes:1024},liveThreshold=.8;
 await page.route('http://127.0.0.1:*/api/**',async route=>{
  const path=new URL(route.request().url()).pathname,body=route.request().postDataJSON()||{};requests.push({path,body});
  let response={ok:true};
  if(path==='/api/status')response={ok:true,agentVersion:manifest.agentVersion,analysisApiVersion:2,vpdlAvailable:true,state:{running:false}};
  if(path==='/api/simulation/analysis')response={ok:true,completed:true,running:false,result:null};
  if(path.startsWith('/api/analysis/')){
   const method=path.split('/').at(-1);response={ok:true,analysisId:id,runId:'test-run',completed:true,running:false,result:method==='dashboard'?model:null};
   if(method==='thresholds')liveThreshold=body.thresholds?.find(value=>value.position==='AN(TOP)'&&value.tool==='Crack')?.value??liveThreshold;
   if(method==='dashboard')response.result={...model,positionToolSummaries:model.positionToolSummaries.map(position=>({...position,tools:position.tools.map(tool=>({...tool,threshold:liveThreshold}))}))};
   if(method==='dates')response={ok:true,page:{rows:[day],start:0,count:1,firstDate:day.date,lastDate:day.date}};
   if(method==='misses')response={ok:true,page:{records:[],hasMore:false}};
   if(method==='tool-images'){
    const start=body.previous?Math.max(1,body.afterId-50):body.afterId+1;
    const records=Array.from({length:Math.min(50,122-start)},(_,i)=>({imageId:start+i,cellId:`P163GG22M21${String(start+i).padStart(5,'0')}`,position:body.position,captureTimestamp:day.date+'T12:00:00',fullPath:`C:\\Exact\\image_${start+i}.jpg`,tools:[{tool:body.tool,result:'NG',score:.9}]}));
    response={ok:true,page:{records,firstId:start,lastId:start+records.length-1,hasMore:start+records.length<=121,hasPrevious:start>1}};
   }
  }
  if(path==='/api/image/preview')response={ok:true,dataUrl:'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5WQAAAAASUVORK5CYII='};
  if(path.startsWith('/api/maintenance/')){
   if(path.endsWith('/settings'))settings=body;
   response={ok:true,settings};
   if(path.endsWith('/preview'))response={...response,planId:'fixture-plan',fileCount:5,totalBytes:1024,reclaimableFiles:2,reclaimableBytes:512};
   if(path.endsWith('/clean'))response={...response,deletedFiles:2,skippedFiles:0,freedBytes:512};
  }
  await route.fulfill({json:response});
 });
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 if(remote)await page.evaluate(id=>window.__VISIONQC_DEBUG__.seedAgentAnalysis(id),id);
 else await page.waitForFunction(()=>window.__VISIONQC_DEBUG__.analysisRestoreReady());
 return requests;
}
test('slow image copy leaves date/threshold/viewer usable and cancels only the independent export',async({page})=>{
 const requests=await setup(page);let copying=false,cancelled=false,starts=0;
 const exportId='independent-export';
 const status=()=>({ok:true,analysisId:id,exportId,operationId:exportId,running:!cancelled,completed:cancelled,phase:copying?'copying':'preparing',copyPrepared:4000,copyTotal:copying?4000:0,copyProcessed:copying?17:0,result:cancelled?{count:4000,files:['export.csv'],images:{copied:17,failed:0,cancelled:true,directory:'C:\\Exports'}}:null});
 await page.route('http://127.0.0.1:*/api/pick/start',r=>r.fulfill({json:{ok:true,path:'C:\\Exports'}}));
 await page.route('http://127.0.0.1:*/api/analysis/export',async r=>{starts++;requests.push({path:'/api/analysis/export',body:r.request().postDataJSON()});await r.fulfill({json:starts===1?status():{ok:true,analysisId:id,exportId:'second',running:false,completed:true,result:{count:121,files:['second.csv']}}});});
 await page.route('http://127.0.0.1:*/api/analysis/export/status',async r=>{requests.push({path:'/api/analysis/export/status',body:r.request().postDataJSON()});await r.fulfill({json:status()});});
 await page.route('http://127.0.0.1:*/api/analysis/export/cancel',async r=>{requests.push({path:'/api/analysis/export/cancel',body:r.request().postDataJSON()});cancelled=true;await r.fulfill({json:status()});});
 await page.locator('[data-vq-action="copy-tool-images"]').first().click();
 const progress=page.locator('.vq4811-export-progress');await expect(progress).toContainText('4,000개 확인');
 copying=true;await expect(progress).toContainText('17 / 4,000');
 const original=requests.find(r=>r.path==='/api/analysis/export').body;expect(original).toMatchObject({independent:true,date:'',position:'AN(TOP)',tool:'Crack',copyImages:true});
 await page.locator('[data-vq-action="dashboard-day"]').click();await expect(page.locator('[data-vq-action="dashboard-day"]')).toHaveAttribute('aria-pressed','true');
 const threshold=page.locator('.vq43-threshold-input').first();await threshold.fill('0.9');await threshold.press('Tab');await expect(threshold).toHaveValue('0.90');
 await page.locator('[data-vq-action="view-tool-images"]').first().click();await expect(page.locator('#vq43-modal')).toHaveClass(/open/);await page.locator('[data-vq-action="close-modal"]').click();
 await page.locator('[data-vq-action="copy-tool-images"]').first().click();await expect(page.locator('#vq43-toast')).toContainText('진행 중');expect(starts).toBe(1);await expect(progress).toBeVisible();
 await progress.locator('button').click();await expect(progress).toHaveCount(0);
 expect(requests.find(r=>r.path==='/api/analysis/export/cancel').body).toEqual({analysisId:id,exportId});expect(requests.filter(r=>r.path==='/api/analysis/cancel')).toHaveLength(0);
 expect(requests.filter(r=>r.path==='/api/analysis/export/status').every(r=>r.body.exportId===exportId)).toBe(true);expect(original.date).toBe('');
 await page.locator('[data-vq-action="download-tool-ng"]').first().click();await expect.poll(()=>starts).toBe(2);
});
test('cancel requested before export acknowledgement is delivered to its export ID',async({page})=>{
 const requests=await setup(page);let releaseStart,cancelled=false;
 const exportId='delayed-export';const status=()=>({ok:true,analysisId:id,exportId,operationId:exportId,running:!cancelled,completed:cancelled,phase:'preparing',result:cancelled?{count:0,files:[],images:{copied:0,failed:0,cancelled:true,directory:'C:\\Exports'}}:null});
 await page.route('http://127.0.0.1:*/api/pick/start',r=>r.fulfill({json:{ok:true,path:'C:\\Exports'}}));
 await page.route('http://127.0.0.1:*/api/analysis/export',async r=>{await new Promise(resolve=>releaseStart=resolve);await r.fulfill({json:status()});});
 await page.route('http://127.0.0.1:*/api/analysis/export/status',r=>r.fulfill({json:status()}));
 await page.route('http://127.0.0.1:*/api/analysis/export/cancel',async r=>{requests.push({path:'/api/analysis/export/cancel',body:r.request().postDataJSON()});cancelled=true;await r.fulfill({json:status()});});
 await page.locator('[data-vq-action="copy-tool-images"]').first().click();const progress=page.locator('.vq4811-export-progress');await expect(progress).toBeVisible();
 await progress.locator('button').click();await expect(progress.locator('button')).toBeDisabled();await expect.poll(()=>!!releaseStart).toBe(true);releaseStart();
 await expect(progress).toHaveCount(0);expect(requests.find(r=>r.path==='/api/analysis/export/cancel').body.exportId).toBe(exportId);
 expect(requests.filter(r=>r.path==='/api/analysis/cancel')).toHaveLength(0);
});
