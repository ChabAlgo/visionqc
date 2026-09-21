import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
const [worker,root]=process.argv.slice(2);
if(!worker||!root)throw new Error('worker and existing isolated lifecycle fixture required');
const prior=JSON.parse(readFileSync(join(root,'report.json'),'utf8'));
const base='http://127.0.0.1:17931';let child;
const env={...process.env,VISIONQC_AGENT_PORT:'17931',VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_WORKER_MODE:'exact'};
const request=async(path,body)=>{const r=await fetch(base+path,{method:body?'POST':'GET',headers:body?{'content-type':'application/json'}:{},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(3000)});const d=await r.json();if(!r.ok||d.ok===false)throw new Error(JSON.stringify(d));return d;};
const start=async()=>{child=spawn(resolve(worker),['--worker'],{cwd:root,windowsHide:true,stdio:'ignore',env});for(let n=0;n<60;n++){if(child.exitCode!==null)throw new Error('Exited '+child.exitCode);try{return await request('/api/status');}catch{}await delay(100);}throw new Error('Startup timeout');};
const stop=async()=>{if(child?.exitCode===null){try{await request('/api/agent/exit',{});}catch{}for(let n=0;n<100&&child.exitCode===null;n++)await delay(100);if(child.exitCode===null){child.kill();throw new Error('Graceful exit failed');}}};
const complete=async(status)=>{for(let n=0;status.running&&n<300;n++){await delay(100);status=await request('/api/analysis/status',{analysisId:prior.analysisId});}if(!status.completed)throw new Error('Analysis restore failed: '+JSON.stringify(status));return status;};
const report={fixture:root,cycles:[],expected:prior.expected};
try{
 try{await request('/api/status');throw new Error('Test port already occupied');}catch(e){if(e.message==='Test port already occupied')throw e;}
 for(let cycle=0;cycle<3;cycle++){
  const begin=Date.now();const health=await start();
  if(health.analysisApiVersion!==1)throw new Error('Analysis capability missing');
  await complete(await request('/api/analysis/status',{analysisId:prior.analysisId}));
  const summary=await complete(await request('/api/analysis/dashboard',{analysisId:prior.analysisId}));
  const history=await request('/api/history/search',{pageSize:100});
  if(summary.result.rawRowCount!==prior.expected||history.totalCount!==prior.expected)throw new Error('Restart duplicated/lost data');
  const duplicate=spawn(resolve(worker),['--worker'],{cwd:root,windowsHide:true,stdio:'ignore',env});
  for(let n=0;n<100&&duplicate.exitCode===null;n++)await delay(100);
  if(duplicate.exitCode!==0){duplicate.kill();throw new Error('Duplicate instance was not rejected');}
  if((await request('/api/status')).instanceId!==health.instanceId)throw new Error('Duplicate replaced running worker');
  report.cycles.push({elapsedMs:Date.now()-begin,rows:summary.result.rawRowCount,version:health.agentVersion});
  await stop();
 }
 report.success=true;
}catch(e){report.success=false;report.error=String(e);process.exitCode=1;}
finally{await stop().catch(()=>{});writeFileSync(join(root,'restart-report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));}
