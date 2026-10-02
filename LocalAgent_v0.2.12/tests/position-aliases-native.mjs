import {spawn} from 'node:child_process';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {setTimeout as delay} from 'node:timers/promises';
import assert from 'node:assert/strict';
const worker=resolve('LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe');
const root=mkdtempSync(join(tmpdir(),'VisionQC-aliases-')),output=join(root,'output'),port=17933,base=`http://127.0.0.1:${port}`;mkdirSync(output);
const request=async(path,body)=>{const response=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const data=await response.json();if(!response.ok||data.ok===false)throw Error(JSON.stringify(data));return data;};
const finish=async status=>{for(let i=0;i<1000&&status.running;i++){await delay(25);status=await request('/api/analysis/status',{analysisId:status.analysisId});}assert.equal(status.completed,true,JSON.stringify(status));return status;};
try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw Error('occupied');}catch(e){if(e.message==='occupied')throw e;}
let child=spawn(worker,[],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
try{
 for(let i=0;i<150;i++){try{await fetch(base+'/api/status');break;}catch{await delay(100);}}
 const csv=join(root,'input.csv');
 writeFileSync(csv,'Date,Time,Cell ID,Position,Total_Result,Crack_result,Crack_score\r\n'+['CA(TOP)','CA_TOP','TCA','OTHER'].map((p,i)=>`2026-02-01,12:00:00,CELL${i},${p},NG,NG,0.9`).join('\r\n'));
 const defs=[{key:'CA_TOP',name:'TCA',aliases:['CA(TOP)','CA_TOP']}];
 const imported=await finish(await request('/api/analysis/import/start',{filePaths:[csv]})),analysisId=imported.analysisId;
 // Save original labels to permanent history before alias configuration.
 await finish(await request('/api/analysis/save-history',{analysisId}));
 let dashboard=await finish(await request('/api/analysis/dashboard',{analysisId,positionDefinitions:defs}));
 assert.equal(dashboard.result.positionSummaries.find(p=>p.position==='TCA').total,3);
 assert.equal(dashboard.result.recordCount,4);
 const exported=await finish(await request('/api/analysis/export',{analysisId,outputDirectory:output,kind:'tool-ng',position:'TCA',tool:'Crack'}));
 assert.equal(exported.result.count,3);assert.ok(readFileSync(exported.result.files[0],'utf8').includes(',TCA,'));
 const history=await request('/api/history/search',{position:'TCA',positionDefinitions:defs});assert.equal(history.totalCount,3);assert.ok(history.items.every(i=>i.position==='TCA'));
 const rawHistory=await request('/api/history/search',{});assert.ok(rawHistory.filterOptions.positions.includes('CA(TOP)'));
 const renamed=[{key:'CA_TOP',name:'TOP_NEW',aliases:['TCA','CA(TOP)','CA_TOP']}];
 dashboard=await finish(await request('/api/analysis/dashboard',{analysisId,positionDefinitions:renamed}));assert.equal(dashboard.result.positionSummaries.find(p=>p.position==='TOP_NEW').total,3);
 // Same definition must not reset the cursor or duplicate rows.
 dashboard=await finish(await request('/api/analysis/dashboard',{analysisId,positionDefinitions:renamed}));assert.equal(dashboard.result.recordCount,4);
 await request('/api/agent/exit',{});for(let i=0;i<50&&child.exitCode===null;i++)await delay(100);assert.notEqual(child.exitCode,null);
 child=spawn(worker,[],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
 for(let i=0;i<150;i++){try{await fetch(base+'/api/status');break;}catch{await delay(100);}}
 await finish(await request('/api/analysis/status',{analysisId}));
 dashboard=await finish(await request('/api/analysis/dashboard',{analysisId}));assert.equal(dashboard.result.positionSummaries.find(p=>p.position==='TOP_NEW').total,3);
 const canonicalImport=await finish(await request('/api/analysis/import/start',{filePaths:[csv],options:{positionDefinitions:defs}}));
 dashboard=await finish(await request('/api/analysis/dashboard',{analysisId:canonicalImport.analysisId}));assert.equal(dashboard.result.positionSummaries.find(p=>p.position==='TCA').total,3);
 let rejected=false;try{await finish(await request('/api/analysis/import/start',{filePaths:[csv],options:{positionDefinitions:[...defs,{key:'X',name:'OTHER',aliases:['CA_TOP']}]}}));}catch(e){rejected=e.message.includes('충돌');}assert.ok(rejected);
 console.log('PASS aliases: existing projection + history read-only grouping, rename, CSV canonical import/export, unknown preservation, duplicate rejection. '+root);
}finally{try{await request('/api/agent/exit',{});}catch{}for(let i=0;i<30&&child.exitCode===null;i++)await delay(100);if(child.exitCode===null)child.kill();}
