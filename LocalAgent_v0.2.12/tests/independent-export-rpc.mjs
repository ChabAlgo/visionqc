import {spawn} from 'node:child_process';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,readdirSync,existsSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
const worker=resolve(process.argv[2]||'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe');
const vpdlRoot=process.env.VISIONQC_TEST_VPDL_ROOT||'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0';
assert.ok(existsSync(join(vpdlRoot,'Cognex Deep Learning Studio','ViDi.NET.Local.dll')),'A matching installed VPDL runtime is required for this optional native RPC fixture');
const root=mkdtempSync(join(resolve('../Temp'),'v4818-rpc-')),port=17935,base=`http://127.0.0.1:${port}`;
try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw Error('Synthetic test port is already occupied');}catch(error){if(error.message.includes('occupied'))throw error;}
const child=spawn(worker,[],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:vpdlRoot,COGNEX_VPDL_DLL_DIR:join(vpdlRoot,'Cognex Deep Learning Studio'),VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
const request=async(path,body)=>{const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(10000)});const data=await response.json();assert.notEqual(data.ok,false,JSON.stringify(data));return data;};
const finish=async job=>{const deadline=Date.now()+10000;while(job.running){assert.ok(Date.now()<deadline,'Synthetic job timeout');await delay(10);job=await request(job.exportId?'/api/analysis/export/status':'/api/analysis/status',{analysisId:job.analysisId,...(job.exportId?{exportId:job.exportId}:{})});}assert.equal(job.completed,true,JSON.stringify(job));return job;};
try{
 let ready;
 for(let i=0;i<40;i++){try{ready=await request('/api/status');break;}catch{await delay(100);}}
 const manifest=JSON.parse(readFileSync('RELEASE_MANIFEST.json'));assert.equal(ready?.agentVersion,manifest.agentVersion);
 const output=join(root,'output'),source=join(root,'source');mkdirSync(output);mkdirSync(source);
 const lines=['Date,Time,Cell ID,Position,Total_result,Crack_result,Crack_score,FullPath'];
 for(let i=0;i<20;i++){const path=join(source,`original_${i}.jpg`);if(i!==0)writeFileSync(path,`synthetic original ${i}`);lines.push(`2026-02-03,12:00:00,P163GG22M21${String(i).padStart(5,'0')},AN(TOP),${i<5?'NG':'OK'},${i<5?'NG':'OK'},.9,${path}`);}
 const input=join(root,'input.csv');writeFileSync(input,lines.join('\n'));
 const initialRequestId=randomUUID().replaceAll('-','');
 const imported=await finish(await request('/api/analysis/import/start',{importRequestId:initialRequestId,filePaths:[input]}));const analysisId=imported.analysisId;
 assert.equal(analysisId,initialRequestId);assert.equal(imported.importRequestId,initialRequestId);assert.equal(imported.importCommitted,true);
 const replayed=await finish(await request('/api/analysis/import/start',{importRequestId:initialRequestId,filePaths:[input]}));assert.equal(replayed.runId,imported.runId);assert.equal(replayed.imported,20);
 const exported=await request('/api/analysis/export',{analysisId,independent:true,kind:'tool-ng',position:'AN(TOP)',tool:'Crack',date:'2026-02-03',outputDirectory:output,copyImages:true,maxRows:3});
 assert.ok(exported.exportId);const main=await request('/api/analysis/status',{analysisId});assert.equal(main.running,false);assert.equal(main.completed,true);
 const dates=await request('/api/analysis/dates',{analysisId});assert.equal(dates.page.summary.totalCount,20);
 const done=await finish(exported);assert.equal(done.result.count,5);assert.equal(done.result.files.length,2);assert.equal(done.result.images.copied,4);assert.equal(done.result.images.failed,1);
 assert.deepEqual(readdirSync(join(output,'AN(TOP)','Crack')).sort(),['original_1.jpg','original_2.jpg','original_3.jpg','original_4.jpg']);
 for(let i=1;i<5;i++)assert.deepEqual(readFileSync(join(source,`original_${i}.jpg`)),readFileSync(join(output,'AN(TOP)','Crack',`original_${i}.jpg`)));
 const cancelled=await request('/api/analysis/export/cancel',{analysisId,exportId:exported.exportId});assert.equal(cancelled.completed,true);assert.equal((await request('/api/analysis/status',{analysisId})).completed,true);
 assert.ok(!readdirSync(output).some(name=>name.startsWith('.copy-queue')));assert.ok(existsSync(input));
 console.log('PASS isolated native Worker RPC: version, independent export/status/cancel routing, main analysis/date availability, split CSV count, exact original names/bytes, failure report and local queue cleanup');
 const added=await finish(await request('/api/analysis/import/start',{analysisId,position:'AN(BOT)',filePaths:[input],fileAssignments:[{path:input,position:'AN(BOT)'}]}));
 assert.equal(added.analysisId,analysisId);assert.equal(added.imported,20);
 let dashboard=(await finish(await request('/api/analysis/dashboard',{analysisId}))).result;
 assert.equal(dashboard.rawRowCount,40);assert.equal(dashboard.uniqueCellCount,20);
 assert.deepEqual(dashboard.positionSummaries.filter(p=>p.input).map(p=>p.total),[20,20]);
 const changed=join(root,'changed.csv');writeFileSync(changed,[lines[0],lines[1]].join('\n'));
 await finish(await request('/api/analysis/import/start',{analysisId,position:'AN(TOP)',filePaths:[changed],fileAssignments:[{path:changed,position:'AN(TOP)'}]}));
 dashboard=(await finish(await request('/api/analysis/dashboard',{analysisId}))).result;
 assert.equal(dashboard.rawRowCount,21);assert.equal(dashboard.positionSummaries.find(p=>p.position==='AN(TOP)').total,1);assert.equal(dashboard.positionSummaries.find(p=>p.position==='AN(BOT)').total,20);
 const revised=await finish(await request('/api/analysis/save-history',{analysisId}));assert.equal(revised.result.saved,true);assert.notEqual(revised.result.runId,imported.runId);
 console.log('PASS isolated native Worker RPC: incremental Position add/replace, same-source explicit assignments, preserved other Position, Cell totals and revised history save');
 const removalId=randomUUID().replaceAll('-','');
 const removalBody={analysisId,position:'AN(BOT)',importRequestId:removalId};
 const removed=await finish(await request('/api/analysis/remove-position',removalBody));assert.equal(removed.importCommitted,true);assert.equal(removed.importRequestId,removalId);
 assert.equal((await finish(await request('/api/analysis/remove-position',removalBody))).importRequestId,removalId);
 assert.equal((await request('/api/analysis/status',{analysisId,action:'restore'})).importCommitted,true);
 dashboard=(await finish(await request('/api/analysis/dashboard',{analysisId}))).result;assert.equal(dashboard.rawRowCount,1);assert.equal(dashboard.positionSummaries.find(p=>p.position==='AN(TOP)').total,1);
 console.log('PASS isolated native Worker RPC: client request identity, acknowledged import replay, Position removal replay, committed receipt and restore routing');
}finally{
 try{await request('/api/shutdown',{});}catch{}
 await delay(200);if(child.exitCode===null)child.kill();
}
