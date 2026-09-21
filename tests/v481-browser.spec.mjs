import {test,expect} from '@playwright/test';

const id='0123456789abcdef0123456789abcdef';
const days=Array.from({length:23},(_,i)=>({date:`2026-02-${String(i+1).padStart(2,'0')}`,total:100,ng:5,ngRate:.05}));
const model={remote:true,recordCount:4000000,rawRowCount:4000000,uniqueCellCount:2000000,ngCellCount:100000,ngCellRate:.05,
 missCount:201,actualUniqueCount:301,matchedActualCount:301,unmatchedActualCount:0,duplicateCount:0,totalNg:200000,unknownRows:0,
 tools:['Crack'],daily:days.slice(-10),dateRange:{firstDate:days[0].date,lastDate:days.at(-1).date,dateCount:23},
 workspaces:[{position:'AN(TOP)',workspaceName:'TestWorkspace.vrws'}],
 positionSummaries:[{position:'AN(TOP)',input:true,total:4000000,rawRows:4000000,ng:200000,ok:3800000,ngRate:.05,actualNg:301,detected:100,misses:201,unmatched:0}],
 positionToolSummaries:[{position:'AN(TOP)',input:true,totalNg:200000,tools:[{tool:'Crack',ng:200000,denominator:200000,rate:1,threshold:.5,minNgScore:.6}]}]};
const cell=n=>`CELL${String(n).padStart(12,'0')}`;
const done=result=>({ok:true,analysisId:id,runId:'test-run',running:false,completed:true,result});
async function setup(page){
 const requests=[];
 await page.route('http://127.0.0.1:*/api/**',async route=>{
   const path=new URL(route.request().url()).pathname;
   const body=route.request().postDataJSON()||{};requests.push({path,body});
   let response={ok:false};
   if(path==='/api/simulation/analysis')response=done(null);
   if(path.startsWith('/api/analysis/')){
     const method=path.split('/').at(-1);
     if(method==='status'||method==='thresholds'||method==='actual-ng')response=done(null);
     if(method==='dashboard')response=done(model);
     if(method==='dates'){
       const start=Math.max(0,Math.min(13,body.dateStart==null||body.dateStart<0?13:body.dateStart));
       response={ok:true,page:{rows:days.slice(start,start+10),start,count:23,firstDate:days[0].date,lastDate:days.at(-1).date}};
     }
     if(method==='misses'){
       const first=body.afterCell?Number(body.afterCell.slice(4))+1:1;
       const records=Array.from({length:Math.min(100,202-first)},(_,i)=>({day:'2026-02-03',cellId:cell(first+i),position:'AN(TOP)',tools:[{tool:'Crack',result:'OK',representativeScore:.7,threshold:.5}]}));
       response={ok:true,page:{records,hasMore:first+records.length<=201,nextDay:'2026-02-03',nextCell:records.at(-1)?.cellId}};
     }
     if(method==='statistics')response=done({total:{count:601,min:.6,max:.6,mean:.6,median:.6},byResult:[{result:'NG',count:601,min:.6,max:.6,mean:.6,median:.6}],bins:[{bin:12,result:'NG',count:601}]});
     if(method==='score-window'){
       const first=body.previous?Math.max(1,body.afterId-300):body.afterId+1;
       const records=Array.from({length:Math.min(300,602-first)},(_,i)=>({observationId:first+i,imageId:first+i,cellId:cell(first+i),position:'AN(TOP)',captureTimestamp:'2026-02-03T12:00:00',fullPath:`C:\\Images\\${first+i}.png`,result:'NG',score:.6}));
       response={ok:true,page:{records,hasMore:first+records.length<=601,nextScore:.6,nextId:records.at(-1)?.observationId,previousScore:.6,previousId:first}};
     }
   }
   await route.fulfill({contentType:'application/json',body:JSON.stringify(response)});
 });
 await page.goto('/index.html?vqDebug=1&browserRegression=1');
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 await page.evaluate(id=>window.__VISIONQC_DEBUG__.seedAgentAnalysis(id),id);
 return requests;
}

test('Agent dashboard keeps four million rows out of browser and pages misses without losing totals',async({page})=>{
 await setup(page);
 let snapshot=await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot());
 expect(snapshot).toMatchObject({remote:true,rows:0,records:0,total:4000000,misses:100});
 await page.locator('[data-vq-action="agent-miss-next"]').click();
 await expect(page.getByRole('button',{name:cell(101),exact:true})).toBeVisible();
 await page.locator('[data-vq-action="agent-miss-next"]').click();
 await expect(page.getByRole('button',{name:cell(201),exact:true})).toBeVisible();
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).misses).toBe(1);
 await page.locator('[data-vq-action="agent-miss-prev"]').click();
 await expect(page.getByRole('button',{name:cell(101),exact:true})).toBeVisible();
 snapshot=await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot());
 expect(snapshot.total).toBe(4000000);expect(snapshot.rows).toBe(0);
});

test('Agent date navigation clamps left boundary and fills last ten dates',async({page})=>{
 await setup(page);
 const previous=page.locator('[data-vq-action="history-chart-prev"][data-chart-step="5"]');
 await previous.click({force:true});await expect.poll(async()=> (await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).dateWindow.start).toBe(8);
 await previous.click({force:true});await expect.poll(async()=> (await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).dateWindow.start).toBe(3);
 await previous.click({force:true});await expect.poll(async()=> (await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).dateWindow.start).toBe(0);
 await page.locator('[data-vq-action="history-chart-latest"]').click();
 await expect.poll(async()=> (await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).dateWindow.start).toBe(13);
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).dateWindow.rows).toHaveLength(10);
});

test('Agent score graph uses exact full count and bounded equal-score pages',async({page})=>{
 const requests=await setup(page);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('analysis'));
 await expect.poll(async()=> (await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).points).toBe(300);
 expect(requests.some(r=>r.path.endsWith('/statistics'))).toBeTruthy();
 expect(requests.filter(r=>r.path.endsWith('/score-window')).every(r=>r.body.pageSize<=300)).toBeTruthy();
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).records).toBe(0);
});

test('live Agent synchronization reconciles complete counts without requesting raw rows and restores its reference',async({page})=>{
 const requests=await setup(page);
 const live=await page.evaluate(()=>window.__VISIONQC_DEBUG__.syncAgentLiveTest(4000000));
 expect(live).toMatchObject({rawRows:4000000,rows:0});
 const final=await page.evaluate(()=>window.__VISIONQC_DEBUG__.syncAgentLiveTest(4000000,true));
 expect(final).toMatchObject({synced:'test-run',rawRows:4000000,rows:0});
 expect(requests.some(r=>r.path==='/api/simulation/analysis')).toBeTruthy();
 expect(requests.some(r=>r.path==='/api/simulation/results')).toBeFalsy();
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).toMatchObject({remote:true,rows:0});
});

test('Blue crop-only completion retains the loaded inspection dashboard',async({page})=>{
 const requests=await setup(page);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.syncAgentLiveTest(100,true,'blue'));
 expect(requests.some(r=>r.path==='/api/simulation/analysis')).toBeFalsy();
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).toMatchObject({total:4000000,remote:true,rows:0});
});

test('ten thousand classification files keep arrow navigation responsive with Agent dashboard loaded',async({page})=>{
 test.setTimeout(90000);
 await setup(page);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('classification'));
 await page.evaluate(()=>{
   const canvas=document.createElement('canvas');canvas.width=1600;canvas.height=1200;
   const context=canvas.getContext('2d');context.fillStyle='#123456';context.fillRect(0,0,1600,1200);
   return new Promise(resolve=>canvas.toBlob(blob=>{
     const transfer=new DataTransfer();
     for(let i=0;i<10000;i++)transfer.items.add(new File([blob],`classification-${String(i).padStart(5,'0')}.png`,{type:'image/png'}));
     const input=document.querySelector('input[type=file][accept="image/*"]');input.files=transfer.files;input.dispatchEvent(new Event('change',{bubbles:true}));resolve();
   },'image/png'));
 });
 await expect(page.locator('main img[alt="classification-00000.png"]').first()).toBeVisible();
 await page.getByRole('heading',{name:'classification-00000.png',exact:true}).click();
 const elapsed=[];
 for(let i=1;i<=30;i++){
   const start=Date.now();await page.keyboard.press('ArrowRight');
   await expect(page.getByRole('heading',{name:`classification-${String(i).padStart(5,'0')}.png`,exact:true})).toBeVisible();
   elapsed.push(Date.now()-start);
 }
 elapsed.sort((a,b)=>a-b);
 console.log('10k classification navigation',JSON.stringify({medianMs:elapsed[15],p95Ms:elapsed[28],maxMs:elapsed[29]}));
 expect(elapsed[28]).toBeLessThan(500);
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).rows).toBe(0);
});
