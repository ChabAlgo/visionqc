import {spawn} from 'node:child_process';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {setTimeout as delay} from 'node:timers/promises';
import assert from 'node:assert/strict';
const worker=resolve(process.argv[2]||'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe');
const root=mkdtempSync(join(tmpdir(),'VisionQC-482-selection-')),port=17932,base=`http://127.0.0.1:${port}`;
try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw Error('Test port already occupied');}catch(error){if(error.message==='Test port already occupied')throw error;}
const output=join(root,'output');mkdirSync(output);
const request=async(path,body)=>{const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(30000)});const data=await response.json();assert.notEqual(data.ok,false,JSON.stringify(data));return data;};
const child=spawn(worker,[],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
const finish=async job=>{while(job.running){await delay(20);job=await request('/api/analysis/status',{analysisId:job.analysisId});}assert.equal(job.completed,true);return job;};
try{
 let status;
 for(let i=0;i<60;i++){try{status=await request('/api/status');break;}catch{await delay(250);}}
 assert.equal(status?.analysisApiVersion,2);
 const lines=['Date,Time,Cell ID,Position,Total_result,WorkspaceKey,FoilDamage_result,FoilDamage_score'];
 for(const pos of ['AN(TOP)','CA(TOP)'])for(let day=1;day<=2;day++)for(let cell=0;cell<20;cell++)for(let duplicate=0;duplicate<2;duplicate++)lines.push(`2026-02-0${day},12:34:56,CELL${cell},${pos},${cell<17?'NG':'OK'},${pos},${cell<17?'NG':'OK'},${cell<10?.9:.7}`);
 const csv=join(root,'input.csv');writeFileSync(csv,lines.join('\r\n'));
 const imported=await finish(await request('/api/analysis/import/start',{filePaths:[csv]}));const analysisId=imported.analysisId;
 const selection={analysisId,kind:'tool-ng',position:'AN(TOP)',tool:'FoilDamage',date:'2026-02-01',outputDirectory:output,maxRows:7};
 const exported=await finish(await request('/api/analysis/export',selection));assert.equal(exported.result.count,17);assert.equal(exported.result.files.length,3);
 assert.equal(exported.result.files.reduce((n,file)=>n+readFileSync(file,'utf8').trim().split(/\r?\n/).length-1,0),17);
 await finish(await request('/api/analysis/thresholds',{analysisId,thresholds:[{position:'AN(TOP)',tool:'FoilDamage',value:.8}]}));
 assert.equal((await finish(await request('/api/analysis/export',selection))).result.count,10);
 const dates=await request('/api/analysis/dates',{analysisId,positions:['AN(TOP)'],date:'2026-02-01'});assert.deepEqual(dates.page.summary,{totalCount:20,ngCount:10,unknown:0});
 await finish(await request('/api/analysis/save-history',{analysisId}));
 const filters={positions:['AN(TOP)'],fromDate:'2026-02-01',toDate:'2026-02-01',pageSize:10};
 const history=await request('/api/history/search',filters);assert.equal(history.totalCount,20);assert.equal(history.items.length,10);
 let job=await request('/api/history/export/start',{filters,outputDirectory:output,maxRows:7});
 do{await delay(20);job=await request('/api/history/export/status',{jobId:job.jobId});}while(job.running);
 assert.equal(job.completed,true);assert.equal(job.result.count,20);assert.equal(job.result.files.length,3);
 assert.equal((await request('/api/history/search',{positions:[]})).totalCount,0);
 writeFileSync(join(root,'report.json'),JSON.stringify({passed:true,toolNg:17,afterThreshold:10,historyPage:10,historyExport:20},null,2));
 console.log('PASS: native HTTP Tool/date/Position export, threshold, history paging, empty selection. '+root);
}finally{
 try{await request('/api/agent/exit',{});}catch{}
 for(let i=0;i<30&&child.exitCode===null;i++)await delay(100);
 if(child.exitCode===null)child.kill();
}
