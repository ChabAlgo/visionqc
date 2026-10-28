import {spawn} from 'node:child_process';
import {mkdtempSync,mkdirSync,readdirSync,copyFileSync,writeFileSync,readFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';
import assert from 'node:assert/strict';
const [worker,workspace,images,positionText='1']=process.argv.slice(2);
assert.ok(worker&&workspace&&images,'worker, test Workspace and test image directory are required');
const positions=Number(positionText),port=17936,base=`http://127.0.0.1:${port}`;
const root=mkdtempSync(join(resolve('../Temp'),'v4822-runtime-')),input=join(root,'input');mkdirSync(input);mkdirSync(join(root,'output'));
const originals=readdirSync(images).filter(name=>/\.(jpg|png|bmp)$/i.test(name)).sort().slice(0,3);assert.equal(originals.length,3);
for(const name of originals)copyFileSync(join(images,name),join(input,name));
const config={mode:'green',agentAnalysis:true,webVersion:'4.8.22',outputRoot:join(root,'output'),parallelPositions:positions>1,autoDistributeGpu:true,maxParallelPositions:4,csvMaxRows:2,csvSplitByDate:true,
 green:{useGpu:true,gpuDevices:'0',keepSubfolders:true,jpegQuality:95,heatmapImageSave:false,printEvery:10,tools:[{toolName:'Crack',threshold:.5,judgement:'Crack'}],judgements:[{name:'Crack',priority:1}]},
 positions:Array.from({length:positions},(_,i)=>({key:'TEST_'+i,displayName:'TEST'+i,enabled:true,greenWorkspacePath:resolve(workspace),greenImageRoot:input,greenImageRoots:[input]}))};
try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw Error('Isolated runtime test port occupied');}catch(error){if(error.message.includes('occupied'))throw error;}
const child=spawn(resolve(worker),['--worker'],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
const request=async(path,body,timeout=15000)=>{const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(timeout)});const result=await response.json();assert.notEqual(result.ok,false,JSON.stringify(result));return result;};
const report={root,positions,expected:originals.length*positions,runs:[]};
try{
 let status;for(let i=0;i<80;i++){assert.equal(child.exitCode,null);try{status=await request('/api/status',undefined,1000);break;}catch{await delay(100);}}
 assert.ok(status);report.agentVersion=status.agentVersion;report.gpu=status.gpu;
 let clock=performance.now();const preload=await request('/api/runtime/preload',config,240000);report.preloadMs=performance.now()-clock;assert.equal(preload.workspaceCount,positions);
 let previousRows;
 for(let run=0;run<3;run++){
  clock=performance.now();const started=await request('/api/simulation/start',config,60000);let state=started.state,deadline=Date.now()+120000;
  while(state.running){assert.ok(Date.now()<deadline,'Simulation timeout');await delay(100);state=(await request('/api/status')).state;}
  assert.ok(!state.error,JSON.stringify(state));assert.equal(state.processed,report.expected);
  const rows=await request('/api/simulation/results',{runId:state.simulationRunId,afterImageId:0,pageSize:1000});
  const normalized=rows.records.map(row=>({position:row.position,cellId:row.cellId,fullPath:row.fullPath,totalResult:row.totalResult,tools:row.tools})).sort((a,b)=>JSON.stringify(a).localeCompare(JSON.stringify(b)));
  if(previousRows)assert.deepEqual(normalized,previousRows,'Repeated inference changed paths, scores or results');previousRows=normalized;
  report.runs.push({wallMs:performance.now()-clock,elapsedSeconds:state.elapsedSeconds,processed:state.processed});
  let ready=await request('/api/status');const reuseDeadline=Date.now()+10000;while(!ready.runtimePreloaded&&Date.now()<reuseDeadline){await delay(50);ready=await request('/api/status');}assert.equal(ready.runtimePreloaded,true,'Completed runtime was not retained');
  await request('/api/simulation/history-decision',{runId:state.simulationRunId,decision:'discard'});
 }
 report.success=true;
}catch(error){
 report.success=false;report.error=String(error);process.exitCode=1;
 if(/라이센스|라이선스|license/i.test(report.error)){
  report.blocked='license';process.exitCode=2;
  try{const first=await request('/api/status');await delay(1000);const second=await request('/api/status');assert.equal(first.running,false);assert.equal(second.runtimePreloaded,false);assert.equal(second.state.elapsedSeconds,0);const csv=join(root,'recovery.csv');writeFileSync(csv,'Cell ID,Position,Total_result,Crack_result,Crack_score\n000001,TEST0,NG,NG,.9');let job=await request('/api/analysis/import/start',{filePaths:[csv]});while(job.running){await delay(50);job=await request('/api/analysis/status',{analysisId:job.analysisId});}assert.equal(job.completed,true);assert.equal(job.imported,1);report.failedRuntimeRecovery=true;}catch(recoveryError){report.recoveryError=String(recoveryError);process.exitCode=1;}
 }
}
finally{try{await request('/api/agent/exit',{});}catch{}for(let i=0;i<25&&child.exitCode===null;i++)await delay(200);if(child.exitCode===null)child.kill();writeFileSync(join(root,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));}
