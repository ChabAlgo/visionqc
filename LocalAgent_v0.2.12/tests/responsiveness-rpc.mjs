import {spawn} from 'node:child_process';
import {mkdtempSync,writeFileSync,readFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import assert from 'node:assert/strict';
const worker=resolve(process.argv[2]||'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe');
const root=mkdtempSync(join(resolve('../Temp'),'v4822-rpc-')),port=17937,base=`http://127.0.0.1:${port}`;
try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw Error('Synthetic port occupied');}catch(error){if(error.message.includes('occupied'))throw error;}
const child=spawn(worker,['--worker'],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
const request=async(path,body)=>{const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});const result=await response.json();assert.notEqual(result.ok,false,JSON.stringify(result));return result;};
const report={root};
try{
 let status;for(let i=0;i<80;i++){try{status=await request('/api/status');break;}catch{await delay(100);}}assert.ok(status);assert.equal(status.agentVersion,JSON.parse(readFileSync('RELEASE_MANIFEST.json')).agentVersion);
 const events=await fetch(base+'/api/events',{signal:AbortSignal.timeout(15000)}),reader=events.body.getReader();let first='';while(!first.includes('\n\n'))first+=new TextDecoder().decode((await reader.read()).value);assert.match(first,/event: status\ndata: \{/);await reader.cancel();
 const rows=100000,path=join(root,'input.csv'),header='Date,Time,Cell ID,Position,Total_result,FullPath,Crack_result,Crack_score,Edge_result,Edge_score,Foil_result,Foil_score,Weld_result,Weld_score';
 writeFileSync(path,[header,...Array.from({length:rows},(_,i)=>`2026-02-03,12:00:00,${String(i).padStart(12,'0')},TCA,NG,C:\\Exact\\${i}.jpg,NG,.9,OK,.1,NG,.8,OK,.2`)].join('\n'));
 let job=await request('/api/analysis/import/start',{filePaths:[path]});const latencies=[];const deadline=Date.now()+180000;
 while(job.running){assert.ok(Date.now()<deadline,'Import timed out');const started=performance.now();await request('/api/status');latencies.push(performance.now()-started);job=await request('/api/analysis/status',{analysisId:job.analysisId});await delay(100);}
 assert.equal(job.completed,true);assert.equal(job.imported,rows);let dashboard=await request('/api/analysis/dashboard',{analysisId:job.analysisId});while(dashboard.running){await delay(10);dashboard=await request('/api/analysis/status',{analysisId:job.analysisId});}
 assert.equal(dashboard.result.rawRowCount,rows);latencies.sort((a,b)=>a-b);report.statusSamples=latencies.length;assert.ok(latencies.length>=5);report.statusP95Ms=latencies[Math.floor((latencies.length-1)*.95)];report.statusMaxMs=latencies.at(-1);assert.ok(report.statusMaxMs<1800,'Status exceeded the UI polling timeout during import');report.rows=rows;report.success=true;
}catch(error){report.success=false;report.error=String(error);process.exitCode=1;}
finally{try{await request('/api/agent/exit',{});}catch{}for(let i=0;i<25&&child.exitCode===null;i++)await delay(200);if(child.exitCode===null)child.kill();writeFileSync(join(root,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));}
