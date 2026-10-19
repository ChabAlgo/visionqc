import {test,expect} from '@playwright/test';
import {readFileSync} from 'node:fs';

const id='0123456789abcdef0123456789abcdef';
const days=Array.from({length:23},(_,i)=>({date:`2026-02-${String(i+1).padStart(2,'0')}`,total:100,ng:5,ngRate:.05}));
const model={remote:true,dailyUnit:'cell',recordCount:4000000,rawRowCount:4000000,uniqueCellCount:2000000,ngCellCount:100000,ngCellRate:.05,
 missCount:201,actualUniqueCount:301,matchedActualCount:301,unmatchedActualCount:0,duplicateCount:0,totalNg:200000,unknownRows:0,
 tools:['Crack'],daily:days.slice(-10),dateRange:{firstDate:days[0].date,lastDate:days.at(-1).date,dateCount:23},
 workspaces:[{position:'AN(TOP)',workspaceName:'TestWorkspace.vrws'}],
 positionSummaries:[{position:'AN(TOP)',input:true,total:4000000,rawRows:4000000,ng:200000,ok:3800000,ngRate:.05,actualNg:301,detected:100,misses:201,unmatched:0}],
 positionToolSummaries:[{position:'AN(TOP)',input:true,totalNg:200000,tools:[{tool:'Crack',ng:200000,denominator:200000,rate:1,threshold:.5,minNgScore:.6}]}]};
const cell=n=>`CELL${String(n).padStart(12,'0')}`;
const done=result=>({ok:true,analysisId:id,runId:'test-run',running:false,completed:true,result});

test('Browser dashboard copies exact inspection paths per Tool, threshold, misses and all without suffix rules',async({page})=>{
 const agentVersion=JSON.parse(readFileSync(new URL('../RELEASE_MANIFEST.json',import.meta.url))).agentVersion;
 const exports=[];
 await page.route('http://127.0.0.1:*/api/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  let json={ok:true};
  if(path==='/api/status')json={ok:true,agentVersion,analysisApiVersion:2,vpdlAvailable:true,state:{running:false}};
  if(path==='/api/pick/start')json={ok:true,path:'C:\\Exports'};
  if(path==='/api/history/export/start'){exports.push(route.request().postDataJSON());json={ok:true,jobId:'exact-copy'};}
  if(path==='/api/history/export/status')json={ok:true,jobId:'exact-copy',completed:true,running:false,result:{images:{copied:1,failed:0,directory:'C:\\Exports'}}};
  await route.fulfill({json});
 });
 const connected=page.waitForResponse(response=>response.url().endsWith('/api/status'));
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await connected;await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 const source=['C:\\Mixed\\capture_ONE.jpg','C:\\Mixed\\capture_TWO.png','C:\\Mixed\\capture_THREE.tif','C:\\Mixed\\capture_FOUR.bmp','C:\\Mixed\\miss.jpeg'];
 const csv=['ImagePath,Date,Time,Cell ID,Total_result,Crack_result,Crack_score,Other_result,Other_score,FullPath,ProcessedPath',
 ...source.map((path,i)=>['C:\\Mixed\\same_cell_preview.png','2026-02-03','12:00:00',i===4?'P163GG22M2100002':'P163GG22M2100001',i===4?'OK':'NG',i===1||i===4?'OK':'NG',i===0?'.9':i===2?'.6':i===3?'':'.2',i===1?'NG':'OK','.99',path,'C:\\Mixed\\processed.png'].join(','))].join('\n');
 await page.evaluate(async text=>{const d=window.__VISIONQC_DEBUG__;await d.seedCsv(text);await d.importNgCellCsv(new File(['Cell ID\nP163GG22M2100002'],'actual.csv'),'AN(TOP)');d.setPage('main');},csv);
 const threshold=page.locator('.vq43-threshold-input[data-position="AN(TOP)"][data-tool="Crack"]');await threshold.fill('0.8');await threshold.press('Tab');
 await page.locator('[data-vq-action="copy-tool-images"][data-position="AN(TOP)"][data-tool="Crack"]').click();await expect.poll(()=>exports.length).toBe(1);
 expect(exports[0].imagePaths).toEqual([source[0],source[3]].map(path=>({path,position:'AN(TOP)'})));
 await page.locator('[data-vq-action="miss-tab"][data-vq-position="AN(TOP)"]').click();
 await page.locator('[data-vq-action="copy-miss-images"]').click();await expect.poll(()=>exports.length).toBe(2);expect(exports[1].imagePaths).toEqual([{path:source[4],position:'AN(TOP)'}]);
 await page.locator('[data-vq-action="copy-all-images"]').click();await expect.poll(()=>exports.length).toBe(3);expect(exports[2].imagePaths.map(e=>e.path)).toEqual(source);
 expect(JSON.stringify(exports)).not.toContain('preview.png');expect(JSON.stringify(exports)).not.toContain('processed.png');
 // Even an existing alternative path must not replace an empty canonical inspection path.
 await page.evaluate(async()=>{await window.__VISIONQC_DEBUG__.seedCsv('ImagePath,Cell ID,Total_result,Crack_result,Crack_score,FullPath\nC:\\Mixed\\preview.png,P163GG22M2100003,NG,NG,.9,');});
 await page.locator('[data-vq-action="copy-all-images"]').click();await expect(page.locator('#vq43-toast')).toContainText('절대 경로');expect(exports).toHaveLength(3);
});
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
       response={ok:true,page:{rows:body.allDates?days:days.slice(start,start+10),start:body.allDates?0:start,count:23,firstDate:days[0].date,lastDate:days.at(-1).date}};
     }
     if(method==='misses'){
       const first=body.afterCell?Number(body.afterCell.slice(4))+1:1;
       const records=Array.from({length:Math.min(100,202-first)},(_,i)=>({day:'2026-02-03',cellId:cell(first+i),position:'AN(TOP)',tools:[{tool:'Crack',result:'OK',representativeScore:.7,threshold:.5}]}));
       response={ok:true,page:{records,hasMore:first+records.length<=201,nextDay:'2026-02-03',nextCell:records.at(-1)?.cellId}};
     }
     if(method==='detail')response={ok:true,page:{records:[{imageId:1,cellId:cell(1),position:'AN(TOP)',captureTimestamp:'2026-02-03T12:00:00',fullPath:'C:\\Images\\1.png',totalResult:'NG',tools:[{tool:'Crack',result:'NG',score:.6}]}]}};
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

test('Multiple CSV picker submits every selected file and exposes dashboard graph and Tool export',async({page})=>{
 const requests=await setup(page);
 const files=['C:\\Exports\\results_001.csv','C:\\Exports\\results_002.csv'];
 await page.route('http://127.0.0.1:*/api/pick/start',async route=>{
  requests.push({path:'/api/pick/start',body:route.request().postDataJSON()});await route.fulfill({json:{ok:true,path:files[0],paths:files}});
 });
 await page.route('http://127.0.0.1:*/api/analysis/import/start',async route=>{
  requests.push({path:'/api/analysis/import/start',body:route.request().postDataJSON()});await route.fulfill({json:done(null)});
 });
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('settings'));
 await page.locator('[data-vq-action="choose-result"]').first().click();
 await expect.poll(()=>requests.filter(r=>r.path==='/api/analysis/import/start').length).toBe(1);
 expect(requests.find(r=>r.path==='/api/analysis/import/start').body.filePaths).toEqual(files);
 expect(requests.find(r=>r.path==='/api/pick/start').body.multiple).toBe(true);
 await expect(page.locator('#vq43-analysis-import-progress')).toHaveCount(0);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('main'));
 await expect(page.locator('[data-vq-action="download-tool-ng"]')).toBeVisible();
 await expect(page.locator('[data-vq-action="download-chart-csv"]')).toBeVisible();
 await expect(page.locator('.vq43-history-point')).toHaveCount(10);
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).toMatchObject({records:0,rows:0,total:4000000});
});

test('Scoped Agent CSV requests preserve date, Tool and Position filters; history exports all matches',async({page})=>{
 const requests=await setup(page);
 await page.route('http://127.0.0.1:*/api/pick/start',route=>route.fulfill({json:{ok:true,path:'C:\\Exports'}}));
 await page.route('http://127.0.0.1:*/api/analysis/export',async route=>{
  requests.push({path:'/api/analysis/export',body:route.request().postDataJSON()});await route.fulfill({json:done({count:17,files:['export.csv']})});
 });
 await page.locator('[data-vq-action="dashboard-day"]').first().click();
 await page.locator('[data-vq-action="download-tool-ng"]').first().click();
 await expect.poll(()=>requests.filter(r=>r.path==='/api/analysis/export').length).toBe(1);
 const tool=requests.find(r=>r.path==='/api/analysis/export').body;
 expect(tool).toMatchObject({kind:'tool-ng',position:'AN(TOP)',tool:'Crack',date:days[13].date});
 await page.locator('[data-chart-position="AN(TOP)"]').uncheck();
 await expect.poll(()=>requests.filter(r=>r.path.endsWith('/dates')).at(-1)?.body.positions).toEqual([]);
 await page.locator('[data-vq-action="download-chart-csv"]').click();
 await expect.poll(()=>requests.filter(r=>r.path==='/api/analysis/export').length).toBe(2);
 expect(requests.filter(r=>r.path==='/api/analysis/export').at(-1).body).toMatchObject({kind:'selection',positions:[],date:days[13].date});
 await page.route('http://127.0.0.1:*/api/history/**',async route=>{
  const path=new URL(route.request().url()).pathname,body=route.request().postDataJSON();requests.push({path,body});
  const data=path.endsWith('/search')?{ok:true,totalCount:205,ngCount:17,uniqueCellCount:205,page:1,items:[],daily:[days[2]],filterOptions:{positions:['AN(TOP)','CA(TOP)'],tools:[],workspaces:[],workspaceTypes:[]}}
   :path.endsWith('/start')?{ok:true,jobId:'history-export'}:{ok:true,jobId:'history-export',completed:true,running:false,result:{count:205,files:['history.csv']}};
  await route.fulfill({json:data});
 });
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('history'));
 await expect(page.locator('[data-chart-position="CA(TOP)"]')).toBeVisible();
 await page.locator('[data-chart-position="CA(TOP)"]').uncheck();
 await expect.poll(()=>requests.filter(r=>r.path==='/api/history/search').at(-1)?.body.positions).toEqual(['AN(TOP)']);
 await page.locator('[data-vq-action="history-day"]').click();
 await expect.poll(()=>requests.filter(r=>r.path==='/api/history/search').at(-1)?.body.fromDate).toBe(days[2].date);
 await expect(page.locator('[data-vq-action="history-refresh"]')).toBeEnabled();
 await page.locator('[data-history-field="fromDate"]').fill('2026-02-01');
 await page.locator('[data-vq-action="download-chart-csv"]').click();
 await expect.poll(()=>requests.filter(r=>r.path==='/api/history/export/start').length).toBe(1);
 expect(requests.find(r=>r.path==='/api/history/export/start').body.filters).toMatchObject({positions:['AN(TOP)'],fromDate:days[2].date,toDate:days[2].date});
 await expect(page.getByText(/205행.*CSV 저장 완료/).first()).toBeVisible();
});

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

test('Score layout stays mounted across delayed live refreshes and reload restore',async({page})=>{
 await setup(page);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('analysis'));
 await expect.poll(async()=> (await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).points).toBe(300);
 for(let i=0;i<3;i++){
  let release;const gate=new Promise(resolve=>release=resolve);let reached=false;
  await page.route('http://127.0.0.1:*/api/analysis/statistics',async route=>{
   reached=true;await gate;
   await route.fulfill({json:done({total:{count:602+i,min:.6,max:.6,mean:.6,median:.6},byResult:[{result:'NG',count:602+i,min:.6,max:.6,mean:.6,median:.6}],bins:[{bin:12,result:'NG',count:602+i}]})});
  });
  await page.evaluate(()=>{window.scoreFrame=document.querySelector('.vq43-analysis-page');window.scoreChart=document.querySelector('.vq43-analysis-scatter');});
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.syncAgentLiveTest(4000000));
  await expect.poll(()=>reached).toBe(true);
  expect(await page.evaluate(()=>window.scoreFrame===document.querySelector('.vq43-analysis-page')&&window.scoreChart===document.querySelector('.vq43-analysis-scatter'))).toBe(true);
  await expect(page.locator('#vq43-agent-score-status')).toContainText('집계');
  release();await expect(page.locator('.vq43-kpi').first()).toContainText(String(602+i));
  await page.unroute('http://127.0.0.1:*/api/analysis/statistics');
 }
 await page.route('http://127.0.0.1:*/api/status',route=>route.fulfill({json:{ok:true,agentVersion:'1.4.7',analysisApiVersion:2,vpdlAvailable:true,state:{running:false}}}));
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.saveAnalysisSnapshot());
 await page.addInitScript(()=>{
  window.scoreLayoutViolations=0;
  new MutationObserver(()=>{const p=document.querySelector('.vq43-analysis-page');if(p&&!p.querySelector('.vq43-analysis-upper-grid'))window.scoreLayoutViolations++;}).observe(document,{childList:true,subtree:true});
 });
 await page.reload();
 await expect(page.locator('.vq43-analysis-upper-grid')).toBeVisible();
 await expect(page.locator('.vq43-kpi').first()).toContainText('601');
 expect(await page.evaluate(()=>window.scoreLayoutViolations)).toBe(0);
 await page.evaluate(()=>{window.__VISIONQC_DEBUG__.setPage('simulation');window.simFrame=document.querySelector('.vq43-sim-page');});
 await expect(page.locator('.vq43-sim-page')).toBeVisible();
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.syncAgentLiveTest(4000000));
 expect(await page.evaluate(()=>window.simFrame===document.querySelector('.vq43-sim-page'))).toBe(true);
});

test('Agent report fetches every date while dashboard stays at ten',async({page})=>{
 const requests=await setup(page);
 const popup=page.waitForEvent('popup');await page.locator('[data-vq-action="export-summary-report"]').click();const report=await popup;
 await expect(report.locator('.chart circle')).toHaveCount(23);await expect(report.locator('.chart')).toHaveCount(2);
 expect(requests.some(r=>r.path.endsWith('/dates')&&r.body.allDates===true)).toBe(true);
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.agentAnalysisSnapshot())).dateWindow.rows).toHaveLength(10);
 await report.close();
});

test('Old Agent Position totals are not labeled as Cell totals',async({page})=>{
 await setup(page);
 const legacy={...model};delete legacy.dailyUnit;
 await page.route('http://127.0.0.1:*/api/analysis/dashboard',route=>route.fulfill({json:done(legacy)}));
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.syncAgentLiveTest(4000000));
 await expect(page.locator('.vq43-main-history-dashboard')).toContainText('업데이트');
 await expect(page.locator('.vq43-main-history-kpis')).toHaveCount(0);
});

test('Unified result selector explains multi-file import, Position binding and Excel limits',async({page})=>{
 await setup(page);await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('settings'));
 const button=page.locator('[data-vq-action="choose-result"]').first();
 await expect(button).toHaveText('파일 선택');await expect(page.locator('[data-vq-action="choose-result-excel"]')).toHaveCount(0);
 await button.hover();await expect(page.locator('#vq43-param-tooltip')).toContainText('CSV와 XLSX');await expect(page.locator('#vq43-param-tooltip')).toContainText('선택한 Position에만');await expect(page.locator('#vq43-param-tooltip')).toContainText('50MB');
 await button.focus();await expect(page.locator('#vq43-param-tooltip')).toHaveClass(/show/);
});

test('Selected TCA and BCA rows retain independent multi-file assignments, labels, replacement and cancellation',async({page})=>{
 await setup(page);page.on('dialog',d=>d.accept());
 await page.evaluate(async()=>{const d=window.__VISIONQC_DEBUG__;await d.renameCustomPosition('CA_TOP','TCA');await d.renameCustomPosition('CA_BOT','BCA');d.setPage('settings');});
 const imports=[],picks=[];let files=['C:\\Logs\\top_001.csv','C:\\Logs\\top_002.xlsx'],bindings=[],fail=false,cancel=false;
 const current=()=>({...model,positionSummaries:['TCA','BCA'].map(position=>({position,input:bindings.some(b=>b.position===position),total:bindings.filter(b=>b.position===position).length,rawRows:bindings.filter(b=>b.position===position).length,ng:0,ngRate:0,actualNg:0,detected:0,misses:0})),positionToolSummaries:[],tools:[]});
 await page.route('http://127.0.0.1:*/api/pick/start',route=>{picks.push(route.request().postDataJSON());return route.fulfill({json:cancel?{ok:false,cancelled:true}:{ok:true,paths:files,path:files[0]}});});
 await page.route('http://127.0.0.1:*/api/analysis/import/start',route=>{const body=route.request().postDataJSON();imports.push(body);if(!fail)bindings=body.fileAssignments;return route.fulfill({json:fail?{ok:false,error:'fixture malformed CSV'}:done(null)});});
 await page.route('http://127.0.0.1:*/api/analysis/dashboard',route=>route.fulfill({json:done(current())}));
 const row=position=>page.locator(`.vq43-input-row[data-vq-position="${position}"]`);
 await row('TCA').locator('[data-vq-action="choose-result"]').click();
 await expect(row('TCA')).toContainText('top_001.csv, top_002.xlsx');await expect(row('BCA')).toContainText('미입력');
 expect(picks[0]).toMatchObject({fileType:'result',multiple:true});expect(imports[0].fileAssignments).toEqual(files.map(path=>({path,position:'TCA'})));
 files=['C:\\Logs\\bottom.csv'];await row('BCA').locator('[data-vq-action="choose-result"]').click();await expect(row('BCA')).toContainText('bottom.csv');
 await expect(row('TCA')).toContainText('top_001.csv, top_002.xlsx');expect(imports[1].fileAssignments).toHaveLength(3);
 files=['C:\\Logs\\top_new.csv'];await row('TCA').locator('[data-vq-action="choose-result"]').click();await expect(row('TCA')).toContainText('top_new.csv');await expect(row('TCA')).not.toContainText('top_001.csv');await expect(row('BCA')).toContainText('bottom.csv');
 expect(imports[2].fileAssignments).toEqual([{path:files[0],position:'TCA'},{path:'C:\\Logs\\bottom.csv',position:'BCA'}]);
 fail=true;files=['C:\\Logs\\broken.csv'];await row('TCA').locator('[data-vq-action="choose-result"]').click();await expect(page.locator('#vq43-toast')).toContainText('malformed CSV');await expect(row('TCA')).toContainText('top_new.csv');await expect(row('BCA')).toContainText('bottom.csv');
 cancel=true;await row('BCA').locator('[data-vq-action="choose-result"]').click();await expect(row('BCA').locator('[data-vq-action="choose-result"]')).toBeEnabled();expect(imports).toHaveLength(4);await expect(row('BCA')).toContainText('bottom.csv');
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.saveAnalysisSnapshot());await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('settings'));await expect(row('TCA')).toContainText('top_new.csv');await expect(row('BCA')).toContainText('bottom.csv');
});

test('Without Agent, one picker imports multiple CSV and XLSX into selected Position and preserves other rows',async({page})=>{
 await page.route('http://127.0.0.1:*/api/**',route=>route.abort());
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 await page.evaluate(async()=>{
  const d=window.__VISIONQC_DEBUG__;await d.renameCustomPosition('CA_TOP','TCA');await d.renameCustomPosition('CA_BOT','BCA');
  d.seedRows([{cellId:'BOTTOM01',position:'BCA',totalResult:'NG',tools:{Crack:{tool:'Crack',result:'NG',score:.9}}}]);
  const csv=new File(['Cell ID,Position,Total_result,Crack_result,Crack_score\nP163GG22M2100001,BCA,NG,NG,0.8'],'top.csv');
  const zip=new JSZip();zip.file('xl/workbook.xml','<workbook xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" r:id="rId1"/></sheets></workbook>');zip.file('xl/_rels/workbook.xml.rels','<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>');
  zip.file('xl/worksheets/sheet1.xml','<worksheet><sheetData><row><c r="A1" t="inlineStr"><is><t>Cell ID</t></is></c><c r="B1" t="inlineStr"><is><t>Total_result</t></is></c><c r="C1" t="inlineStr"><is><t>Crack_score</t></is></c></row><row><c r="A2" t="inlineStr"><is><t>P163GG22M2100002</t></is></c><c r="B2" t="inlineStr"><is><t>OK</t></is></c><c r="C2"><v>0.123456789</v></c></row></sheetData></worksheet>');
  const excel=new File([await zip.generateAsync({type:'uint8array'})],'top.xlsx');
  const root=await navigator.storage.getDirectory(),handles=[];
  for(const file of [csv,excel]){const handle=await root.getFileHandle(file.name,{create:true}),writer=await handle.createWritable();await writer.write(file);await writer.close();handles.push(handle);}
  window.resultPickerOptions=null;window.showOpenFilePicker=async options=>{window.resultPickerOptions=options;return handles;};d.setPage('settings');
 });
 const row=p=>page.locator(`.vq43-input-row[data-vq-position="${p}"]`);
 await row('TCA').locator('[data-vq-action="choose-result"]').click();await expect(row('TCA')).toContainText('top.csv, top.xlsx');await expect(row('BCA')).toContainText('debug.csv');
 expect(await page.evaluate(()=>window.resultPickerOptions.multiple)).toBe(true);
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot())).summaries.find(p=>p.position==='TCA').total).toBe(2);
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot())).summaries.find(p=>p.position==='BCA').total).toBe(1);
 await page.evaluate(()=>window.showOpenFilePicker=async()=>{throw new DOMException('cancel','AbortError');});await row('TCA').locator('[data-vq-action="choose-result"]').click();await expect(row('TCA').locator('[data-vq-action="choose-result"]')).toBeEnabled();await expect(row('TCA')).toContainText('top.csv, top.xlsx');
});

test('Agent score layout preserves original two-column controls in both themes',async({page})=>{
 await page.setViewportSize({width:1920,height:1080});
 await setup(page);
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('analysis'));
 await expect(page.locator('.vq43-analysis-upper-grid')).toBeVisible();
 for(const theme of ['dark','light']){
  const geometry=await page.evaluate(()=>{
   const box=s=>document.querySelector(s).getBoundingClientRect();
   const left=box('.vq43-analysis-left'),right=box('.vq43-analysis-right');
   const charts=[...document.querySelectorAll('.vq43-chart-card')].map(e=>e.getBoundingClientRect());
   return {left:left.x,right:right.x,sameTop:Math.abs(left.y-right.y)<2,
    leftOrder:[...document.querySelector('.vq43-analysis-left').children].map(e=>e.className),
    rightOrder:[...document.querySelector('.vq43-analysis-right').children].map(e=>e.className),
    chartsAligned:Math.abs(charts[0].y-charts[1].y)<2,chartsVisible:charts.every(b=>b.bottom<=innerHeight),
    noOverflow:document.documentElement.scrollWidth<=innerWidth};
  });
  expect(geometry.left).toBeLessThan(geometry.right);
  expect(geometry.sameTop).toBe(true);
  expect(geometry.leftOrder).toEqual(['vq43-filter','vq43-note vq43-analysis-scope-note','vq43-kpi-grid']);
  expect(geometry.rightOrder).toEqual(['vq43-analysis-export','vq43-actual-ng-minimum','vq43-table vq43-summary-table']);
  expect(geometry.chartsAligned).toBe(true);expect(geometry.chartsVisible).toBe(true);expect(geometry.noOverflow).toBe(true);
  if(theme==='light')await expect(page.locator('.vq43-chart-expand-btn')).toHaveCSS('color','rgb(30, 64, 175)');
  await page.screenshot({path:`test-results/v484-score-${theme}.png`,fullPage:true});
  if(theme==='dark')await page.locator('[data-vq-action="theme-toggle"]').click();
 }
 await page.locator('[data-agent-score-page="1"]').click();
 await expect(page.locator('.vq43-history-navigation')).toContainText('301–600 / 601');
 await page.locator('[data-agent-score-page="-1"]').click();
 await expect(page.locator('.vq43-history-navigation')).toContainText('1–300 / 601');
 await page.locator('[data-vq-action="open-chart-modal"]').click();
 await expect(page.locator('#vq43-chart-modal')).toHaveClass(/open/);
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

test('Agent alphabetic summaries render in configured order for cards, tools and date selectors',async({page})=>{
 const originalPositions=model.positionSummaries, originalTools=model.positionToolSummaries;
 const alphabetical=['AN(BOT)','AN(TOP)','CA(BOT)','CA(TOP)'];
 model.positionSummaries=alphabetical.map(position=>({...originalPositions[0],position}));
 model.positionToolSummaries=alphabetical.map(position=>({...originalTools[0],position}));
 try {
  await setup(page);
  const expected=['CA(TOP)','AN(TOP)','CA(BOT)','AN(BOT)'];
  await expect(page.locator('.vq43-position-card .vq43-card-head strong')).toHaveText(expected);
  await expect(page.locator('.vq43-tool-position-card .vq43-card-head strong')).toHaveText(expected);
  expect(await page.locator('[data-chart-scope="main"][data-chart-position]').evaluateAll(elements=>elements.map(e=>e.dataset.chartPosition))).toEqual(expected);
 } finally {model.positionSummaries=originalPositions;model.positionToolSummaries=originalTools;}
});

test('Image copy sends the same Tool selection, descriptive label, progress and cancellation',async({page})=>{
 const requests=await setup(page);let cancelled=false,started=false;
 await page.route('http://127.0.0.1:*/api/pick/start',r=>r.fulfill({json:{ok:true,path:'C:\\Exports'}}));
 await page.route('http://127.0.0.1:*/api/analysis/export',async r=>{requests.push({path:'/api/analysis/export',body:r.request().postDataJSON()});started=true;await r.fulfill({json:{ok:true,analysisId:id,running:true,completed:false,copyProcessed:1,copyTotal:20}});});
 await page.route('http://127.0.0.1:*/api/analysis/cancel',async r=>{cancelled=true;await r.fulfill({json:{ok:true}});});
 await page.route('http://127.0.0.1:*/api/analysis/status',r=>r.fulfill({json:!started?done(null):cancelled?done({count:20,files:['result.csv'],images:{copied:1,failed:0,cancelled:true,directory:'C:\\Exports\\Images'}}):{ok:true,analysisId:id,running:true,copyProcessed:1,copyTotal:20}}));
 await page.locator('[data-vq-action="copy-tool-images"]').click();
 await expect(page.locator('.vq4811-export-progress')).toContainText('1 / 20');
 const body=requests.find(r=>r.path==='/api/analysis/export').body;expect(body).toMatchObject({copyImages:true,kind:'tool-ng',position:'AN(TOP)',tool:'Crack'});expect(body.exportLabel).toContain('AN(TOP)_Crack_NG_Threshold0.50_전체날짜');
 await page.locator('.vq4811-export-progress button').click();await expect(page.locator('.vq4811-export-progress')).toHaveCount(0);expect(cancelled).toBe(true);
});

for(const outcome of ['save','cancel','error'])test(`Agent score single-image ${outcome} uses file picker and original filename only`,async({page})=>{
 const requests=await setup(page);
 await page.route('http://127.0.0.1:*/api/pick/start',async route=>{requests.push({path:'/api/pick/start',body:route.request().postDataJSON()});await route.fulfill({json:outcome==='cancel'?{ok:false,cancelled:true,path:''}:{ok:true,path:'C:\\Exports\\1.png'}});});
 await page.route('http://127.0.0.1:*/api/image/copy-single',async route=>{requests.push({path:'/api/image/copy-single',body:route.request().postDataJSON()});await route.fulfill({json:outcome==='error'?{ok:false,error:'fixture missing source'}:{ok:true,path:'C:\\Exports\\1.png'}});});
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('analysis'));
 await expect.poll(()=>page.locator('.vq43-scatter-point').count()).toBe(300);
 await page.locator('.vq43-scatter-point').first().dispatchEvent('click');await expect(page.locator('#vq43-modal')).toHaveClass(/open/);
 await page.locator('[data-vq-action="copy-single-image"]').click();
 await expect.poll(()=>requests.filter(r=>r.path==='/api/pick/start').length).toBe(1);
 expect(requests.find(r=>r.path==='/api/pick/start').body).toMatchObject({kind:'save-image',suggestedName:'1.png'});
 if(outcome==='cancel'){await expect(page.locator('[data-vq-action="copy-single-image"]')).toBeEnabled();expect(requests.filter(r=>r.path==='/api/image/copy-single')).toHaveLength(0);}
 else{await expect.poll(()=>requests.filter(r=>r.path==='/api/image/copy-single').length).toBe(1);expect(requests.find(r=>r.path==='/api/image/copy-single').body).toMatchObject({sourcePath:'C:\\Images\\1.png',targetPath:'C:\\Exports\\1.png'});}
 expect(requests.filter(r=>r.path==='/api/history/export/start'||r.path==='/api/analysis/export')).toHaveLength(0);
 await expect(page.locator('#vq43-modal')).toHaveClass(/open/);
});

test('all analysis bulk image buttons consistently choose a folder and pass the intended group',async({page})=>{
 const requests=await setup(page);
 await page.route('http://127.0.0.1:*/api/pick/start',async route=>{requests.push({path:'/api/pick/start',body:route.request().postDataJSON()});await route.fulfill({json:{ok:true,path:'C:\\Exports'}});});
 await page.route('http://127.0.0.1:*/api/analysis/export',async route=>{requests.push({path:'/api/analysis/export',body:route.request().postDataJSON()});await route.fulfill({json:done({count:1,files:['x.csv'],images:{copied:1,failed:0,directory:'C:\\Exports'}})});});
 for(const [action,group] of [['copy-tool-images','Crack'],['copy-miss-images','미검Cell'],['copy-chart-images','Threshold적용_전체날짜'],['copy-all-images','전체결과'],['copy-score-images','Crack']]){
  if(action==='copy-score-images'){await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('analysis'));await expect.poll(()=>page.locator('.vq43-scatter-point').count()).toBe(300);}
  const before=requests.filter(r=>r.path==='/api/analysis/export').length;await page.locator(`[data-vq-action="${action}"]`).first().click();
  await expect.poll(()=>requests.filter(r=>r.path==='/api/analysis/export').length).toBe(before+1);
  expect(requests.filter(r=>r.path==='/api/analysis/export').at(-1).body).toMatchObject({copyImages:true,imageGroup:group});
  expect(requests.filter(r=>r.path==='/api/pick/start').at(-1).body.kind).toBe('folder');
 }
 await page.route('http://127.0.0.1:*/api/history/**',async route=>{
  const path=new URL(route.request().url()).pathname,body=route.request().postDataJSON();requests.push({path,body});
  await route.fulfill({json:path.endsWith('/search')?{ok:true,totalCount:1,ngCount:1,uniqueCellCount:1,page:1,items:[],daily:[days[2]],filterOptions:{positions:['AN(TOP)'],tools:[],workspaces:[],workspaceTypes:[]}}:path.endsWith('/start')?{ok:true,jobId:'copy-history'}:{ok:true,jobId:'copy-history',completed:true,running:false,result:{count:1,files:['history.csv'],images:{copied:1,failed:0,directory:'C:\\Exports'}}}});
 });
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('history'));await expect(page.locator('[data-chart-position="AN(TOP)"]')).toBeVisible();
 await page.locator('[data-vq-action="copy-chart-images"]').click();await expect.poll(()=>requests.filter(r=>r.path==='/api/history/export/start').length).toBe(1);
 expect(requests.find(r=>r.path==='/api/history/export/start').body).toMatchObject({copyImages:true,imageGroup:expect.stringMatching(/^검사이력_/)});
 expect(requests.filter(r=>r.path==='/api/pick/start').at(-1).body.kind).toBe('folder');
});
