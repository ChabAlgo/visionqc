import {test,expect} from '@playwright/test';
import {readFileSync} from 'node:fs';
const manifest=JSON.parse(readFileSync(new URL('../RELEASE_MANIFEST.json',import.meta.url)));
const id='0123456789abcdef0123456789abcdef';
const day={date:'2026-02-03',total:121,ng:121,ngRate:1};
const model={remote:true,dailyUnit:'cell',recordCount:121,rawRowCount:121,uniqueCellCount:121,ngCellCount:121,ngCellRate:1,missCount:0,actualUniqueCount:0,matchedActualCount:0,unmatchedActualCount:0,duplicateCount:0,totalNg:121,unknownRows:0,tools:['Crack'],daily:[day],dateRange:{firstDate:day.date,lastDate:day.date,dateCount:1},workspaces:[],positionSummaries:[{position:'AN(TOP)',input:true,total:121,rawRows:121,ng:121,ok:0,ngRate:1,actualNg:0,detected:0,misses:0,unmatched:0}],positionToolSummaries:[{position:'AN(TOP)',input:true,totalNg:121,tools:[{tool:'Crack',ng:121,denominator:121,rate:1,threshold:.8,minNgScore:null}]}]};
async function setup(page,remote=true){
 const requests=[];let settings={automatic:true,retentionDays:30,maxMegabytes:1024};
 await page.route('http://127.0.0.1:*/api/**',async route=>{
  const path=new URL(route.request().url()).pathname,body=route.request().postDataJSON()||{};requests.push({path,body});
  let response={ok:true};
  if(path==='/api/status')response={ok:true,agentVersion:manifest.agentVersion,analysisApiVersion:2,vpdlAvailable:true,state:{running:false}};
  if(path==='/api/simulation/analysis')response={ok:true,completed:true,running:false,result:null};
  if(path.startsWith('/api/analysis/')){
   const method=path.split('/').at(-1);response={ok:true,analysisId:id,runId:'test-run',completed:true,running:false,result:method==='dashboard'?model:null};
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
test('Tool image button precedes copy, pages exact paths and supports keyboard boundaries',async({page})=>{
 const requests=await setup(page);
 const view=page.locator('[data-vq-action="view-tool-images"]').first(),copy=page.locator('[data-vq-action="copy-tool-images"]').first();
 expect(await view.getAttribute('title')).toContain('Threshold');
 expect(await view.evaluate((el)=>el.nextElementSibling.dataset.vqAction)).toBe('copy-tool-images');
 for(const width of [1920,1280]){await page.setViewportSize({width,height:1080});const a=await view.boundingBox(),b=await copy.boundingBox();expect(a.x+a.width<=b.x+1||a.y+a.height<=b.y+1).toBe(true);}
 await page.locator('[data-vq-action="dashboard-day"]').click();await view.click();
 await expect(page.locator('#vq43-modal')).toHaveClass(/open/);await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\image_1.jpg');
 expect(requests.filter(r=>r.path.endsWith('/tool-images'))[0].body).toMatchObject({date:day.date,position:'AN(TOP)',tool:'Crack',pageSize:50,afterId:0});
 await page.keyboard.press('ArrowLeft');await expect(page.locator('#vq43-modal-viewport')).toContainText('첫 이미지');
 for(let i=0;i<50;i++)await page.keyboard.press('ArrowRight');
 await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\image_51.jpg');
 await page.keyboard.press('ArrowLeft');await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\image_50.jpg');
 expect(requests.filter(r=>r.path.endsWith('/tool-images')).at(-1).body).toMatchObject({afterId:51,previous:true,pageSize:50});
 await page.keyboard.press('ArrowRight');await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\image_51.jpg');
 for(let i=0;i<49;i++)await page.keyboard.press('ArrowRight');
 await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\image_100.jpg');
 await page.keyboard.press('ArrowRight');await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\image_101.jpg');
 for(let i=0;i<20;i++)await page.keyboard.press('ArrowRight');
 await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\image_121.jpg');
 await page.keyboard.press('ArrowRight');await expect(page.locator('#vq43-modal-viewport')).toContainText('마지막');
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot().records)).toBe(0);
 await page.locator('[data-vq-action="close-modal"]').click();await expect(page.locator('#vq43-modal')).not.toHaveClass(/open/);
});
test('browser-imported Tool viewer uses selected date, final threshold and exact original paths',async({page})=>{
 await setup(page,false);
 const csv=['Date,Time,Cell ID,Total_result,Crack_result,Crack_score,Other_result,Other_score,FullPath',
 '2026-02-03,12:00:00,P163GG22M2100001,NG,NG,.9,OK,.1,C:\\Exact\\original.jpg',
 '2026-02-03,12:00:00,P163GG22M2100001,NG,OK,.99,NG,.99,C:\\Exact\\heatmap.jpg',
 '2026-02-03,12:00:00,P163GG22M2100001,NG,NG,.6,OK,.1,C:\\Exact\\below.jpg',
 '2026-02-04,12:00:00,P163GG22M2100002,NG,NG,.9,OK,.1,C:\\Exact\\other-day.jpg'].join('\n');
 await page.evaluate(async csv=>{await window.__VISIONQC_DEBUG__.seedCsv(csv);window.__VISIONQC_DEBUG__.setPage('main');},csv);
 await page.locator('.vq43-threshold-input[data-position="AN(TOP)"][data-tool="Crack"]').fill('0.8');await page.locator('.vq43-threshold-input[data-position="AN(TOP)"][data-tool="Crack"]').press('Tab');
 await page.locator('[data-vq-action="dashboard-day"][data-vq-history-day="2026-02-03"]').click();
 await page.locator('[data-vq-action="view-tool-images"][data-position="AN(TOP)"][data-tool="Crack"]').click();
 await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\original.jpg');
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.modalSequenceSnapshot().count)).toBe(1);
 await page.keyboard.press('ArrowRight');await expect(page.locator('.vq43-modal-path span')).toHaveText('C:\\Exact\\original.jpg');
});
test('maintenance defaults to 30 days; preview is read-only and clean uses reviewed token',async({page})=>{
 const requests=await setup(page);await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('settings'));
 await expect(page.locator('#vq4817-log-days')).toHaveValue('30');await expect(page.locator('[data-vq-action="maintenance-save"]')).toBeEnabled();
 await expect(page.locator('[data-vq-action="maintenance-clean"]')).toBeDisabled();
 await page.locator('[data-vq-action="maintenance-preview"]').click();await expect(page.locator('.vq4817-maintenance-summary')).toContainText('정리 가능');
 expect(requests.filter(r=>r.path.endsWith('/clean'))).toHaveLength(0);
 await page.locator('[data-vq-action="maintenance-clean"]').click();await expect(page.locator('.vq4817-maintenance-summary')).toContainText('2개 정리');
 expect(requests.find(r=>r.path.endsWith('/clean')).body).toEqual({planId:'fixture-plan'});
 await page.locator('#vq4817-log-days').fill('14');await page.locator('[data-vq-action="maintenance-save"]').click();await expect(page.locator('.vq4817-maintenance-summary')).toContainText('저장했습니다');
 expect(requests.find(r=>r.path.endsWith('/settings')).body).toMatchObject({retentionDays:14,automatic:true});await expect(page.locator('[data-vq-action="maintenance-clean"]')).toBeDisabled();
 await page.locator('#vq4817-log-days').fill('0');await page.locator('[data-vq-action="maintenance-save"]').click();await expect(page.locator('#vq43-toast')).toContainText('1~3650');
 expect(requests.filter(r=>r.path.endsWith('/settings'))).toHaveLength(1);
});
