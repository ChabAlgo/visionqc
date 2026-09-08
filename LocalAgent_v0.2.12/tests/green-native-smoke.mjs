import { DatabaseSync } from 'node:sqlite';
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync, copyFileSync, readdirSync, existsSync, cpSync, statSync } from 'node:fs';
import { join, basename, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { setTimeout as sleep } from 'node:timers/promises';

const [worker, product='4.0', label='universal', existingFixture, profile='normal', offlineWebSource] = process.argv.slice(2);
if (!worker) throw new Error('worker path required');
const base='http://127.0.0.1:17891';
const reportRoot=mkdtempSync(join(tmpdir(),'VisionQC-green-'+label+'-'));
if (offlineWebSource) cpSync(resolve(offlineWebSource), join(reportRoot, 'Web'), { recursive: true });
const log=(event,data={})=>console.log(JSON.stringify({time:new Date().toISOString(),event,...data}));
const request=async(path,body,timeout=10000)=>{
  const r=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:body===undefined?{}:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(timeout)});
  const data=await r.json(); if(!r.ok || data.ok===false) throw new Error(path+': '+JSON.stringify(data)); return data;
};
const installedDb=join(process.env.LOCALAPPDATA,'VisionQC','LocalAgent','data','visionqc-history.sqlite');
let config, fixture;
if(existingFixture){fixture=resolve(existingFixture); config=JSON.parse(readFileSync(join(fixture,'request.json'),'utf8'));}
else {
  const db=new DatabaseSync(installedDb,{readOnly:true});
  const row=db.prepare('SELECT config_json FROM runs WHERE source_type=? AND mode=? AND status=? ORDER BY started_at_utc DESC LIMIT 1').get('simulation','green','completed');
  db.close(); if(!row) throw new Error('No completed green config');
  config=JSON.parse(row.config_json); fixture=reportRoot;
  const input=join(fixture,'input'); mkdirSync(input);
  const source=config.green.keywordInputRoots[0];
  const images=readdirSync(source).filter(n=>/\.(jpg|jpeg|png|bmp)$/i.test(n)).sort().slice(0,3);
  if(images.length===0) throw new Error('No source images');
  for(const name of images) copyFileSync(join(source,name),join(input,name));
  config.green.keywordMode=false; config.green.keywordInputRoot=''; config.green.keywordInputRoots=[]; config.green.cellIdCsvPath='';
  config.green.heatmapImageSave=true; config.green.printEvery=1;
  for(const position of config.positions){position.greenImageRoot=input;position.greenImageRoots=[input];position.greenKeyword='';}
  writeFileSync(join(fixture,'request.json'),JSON.stringify(config,null,2));
}
config.green.disableTensorRt=profile==='notrt'||profile==='both';
config.green.freshRuntime=profile==='fresh'||profile==='both';
config.green.detailedDiagnostics=profile!=='quiet';
config.green.disableOptimizedGpuMemory=profile==='memory';
if(profile==='bad'){
  const input=join(reportRoot,'invalid-input');mkdirSync(input);
  writeFileSync(join(input,'20260807_074705_J1037G87P611903999.jpg'),'Intentionally invalid image for failure-stage regression');
  for(const p of config.positions){p.greenImageRoot=input;p.greenImageRoots=[input];}
}
const webVersion=JSON.parse(readFileSync(new URL('../../package.json',import.meta.url),'utf8')).version;
config.webVersion=webVersion;
config.outputRoot=join(reportRoot,'output');mkdirSync(config.outputRoot);
writeFileSync(join(reportRoot,'request.json'),JSON.stringify(config,null,2));
log('PREPARED',{reportRoot,fixture,worker,product,positions:config.positions.map(p=>({name:p.displayName,workspace:p.greenWorkspacePath})),input:config.positions[0].greenImageRoot});
try {
  const status=await request('/api/status');
  if(status.running) throw new Error('Existing Agent is running a simulation; refusing to stop it');
  await request('/api/agent/exit',{});
  for(let i=0;i<30;i++){await sleep(400);try{await request('/api/status',undefined,1000);}catch{break;}}
} catch(e){if(!/fetch failed|ECONNREFUSED/.test(String(e)) && !String(e).includes('exited'))throw e;}
const studio=join('C:\\Program Files\\Cognex\\VisionPro Deep Learning',product,'Cognex Deep Learning Studio');
const api=product==='4.0'?'8.0':product==='4.1'?'8.1':'8.2';
const childStartedAt=Date.now();
const child=spawn(resolve(worker),['--worker'],{cwd:reportRoot,windowsHide:true,stdio:['ignore','pipe','pipe'],env:{...process.env,VISIONQC_AGENT_HOME:reportRoot,VISIONQC_HISTORY_DB_PATH:join(reportRoot,'history.sqlite'),COGNEX_VPDL_ROOT:join('C:\\Program Files\\Cognex\\VisionPro Deep Learning',product),COGNEX_VPDL_DLL_DIR:studio,VISIONQC_VPDL_API_VERSION:api,VISIONQC_VPDL_PRODUCT_VERSION:product,VISIONQC_VPDL_WORKER_MODE:label}});
child.stdout.on('data',d=>log('STDOUT',{text:String(d)}));child.stderr.on('data',d=>log('STDERR',{text:String(d)}));child.on('exit',(code,signal)=>log('WORKER_EXIT',{code,signal}));
let keepAlive=setInterval(()=>log('WAIT',{pid:child.pid}),15000);let result={reportRoot,fixture,product,label};
try {
  let status;for(let i=0;i<20;i++){if(child.exitCode!==null)throw new Error('Worker exited before ready: '+child.exitCode);try{status=await request('/api/status',undefined,15000);if(status.license!=='확인 중')break;}catch(e){log('STARTUP_WAIT',{error:String(e)});}await sleep(500);}
  if(!status)throw new Error('Worker did not open server');
  log('READY',{agentVersion:status.agentVersion,api:status.activeVpdlApiVersion,license:status.license,mode:status.vpdlWorkerMode});
  if(status.license==='Runtime Error')throw new Error(status.runtimeMessage);
  if(offlineWebSource){
    const page=await fetch(base+'/',{signal:AbortSignal.timeout(5000)});
    const html=await page.text();
    const extension=await fetch(base+'/visionqc-extension.js',{signal:AbortSignal.timeout(5000)});
    if(!page.ok || !html.includes(webVersion) || !extension.ok) throw new Error('Packaged offline web assets failed');
    result.offlineWebVerified=true;
    log('OFFLINE_WEB_VERIFIED',{pageStatus:page.status,extensionStatus:extension.status});
  }
  for(let i=0;i<5;i++){
    const idle=await request('/api/runtime/check',{useGpu:true,gpuDevices:'0'});
    if(!idle.deferred || idle.licenseVerified!==false) throw new Error('Idle check incorrectly initialized runtime');
  }
  const naming=await request('/api/naming/preview',{profile:{dateTime:{mode:'auto'},delimiter:'_',cellId:{mode:'auto',candidateLength:18,extractLength:16,requireLetter:true}},fileNames:['TAB_J1037G87P611903999_20260807074705_CRACK AN(TOP)_BLUTOL.jpg']});
  if(naming.records[0].captureTimestamp!=='2026-08-07T07:47:05') throw new Error('Timestamp API failed');
  result.timestampPreview=naming.records[0];
  for(const position of (process.env.VISIONQC_TEST_PREVIEW === '1' ? config.positions.filter(p=>p.enabled!==false) : [])){
    const structure=await request('/api/workspace/inspect',{path:position.greenWorkspacePath,useGpu:true,gpuDevices:'0'},120000);
    if(!structure.streams?.length) throw new Error('Metadata inspection returned no streams');
    log('METADATA_VERIFIED',{position:position.displayName,method:structure.loadMethod});
  }
  for(let i=0;i<3;i++){
    const idle=await request('/api/runtime/check',{useGpu:true,gpuDevices:'0'});
    if(!idle.deferred) throw new Error('Metadata preview became an initialized simulation');
  }
  result.idleChecksVerified=true;
  result.explicitMetadataVerified=process.env.VISIONQC_TEST_PREVIEW === '1';
  const preload=await request('/api/runtime/preload',config,240000);log('PRELOADED',{workspaceCount:preload.workspaceCount,elapsedMs:preload.elapsedMs});
  const start=await request('/api/simulation/start',config,30000);log('STARTED',{state:start.state});
  let final; for(let i=0;i<150;i++){if(child.exitCode!==null)throw new Error('Worker crashed during simulation: '+child.exitCode);await sleep(1000);try{status=await request('/api/status',undefined,5000);}catch(e){if(child.exitCode!==null)throw e;continue;}if(!status.running){final=status.state;break;}}
  if(!final)throw new Error('Simulation deadline exceeded');
  result={...result,final};log('RESULT',{state:final});
  if(profile==='bad'){
    const failure=readFileSync(join(reportRoot,'logs','last-sdk-failure.txt'),'utf8');
    if(!final.error || !failure.includes('Stage=Image.Load') || !failure.includes('HResult=0x')) throw new Error('Expected image-load failure was not diagnosed');
    result.expectedFailureVerified=true;
  } else if(final.error)throw new Error(final.error);
  await sleep(1000); const after=await request('/api/status');
  result.runtimePreloadedAfter=after.runtimePreloaded;
  if(['fresh','notrt','both','bad'].includes(profile)&&after.runtimePreloaded) throw new Error('Compatibility runtime was incorrectly cached');
  if(profile!=='bad' && !(final.processed>0))throw new Error('No images processed');
  const workerLogs=readdirSync(join(reportRoot,'logs')).filter(n=>n.startsWith('agent-worker-')&&n.endsWith('.log')).map(n=>readFileSync(join(reportRoot,'logs',n),'utf8')).join('\n');
  if(profile!=='quiet' && !workerLogs.includes('SDK_BEGIN')) throw new Error('Detailed stage log missing');
  if(profile==='memory' && !workerLogs.includes('Explicit OptimizedGPUMemory(0) call succeeded')) throw new Error('Memory policy missing');
  result.diagnosticStagesVerified=workerLogs.includes('SDK_BEGIN');
  const sdkLogs=join(process.env.APPDATA,'Cognex Corporation','Cognex VisionPro Deep Learning '+product,'logs');
  result.nativeSdkLogDirectory=sdkLogs;
  result.nativeSdkLogs=existsSync(sdkLogs)?readdirSync(sdkLogs).filter(n=>n.startsWith(basename(worker,'.exe').toLowerCase()+'_')&&n.endsWith('.debug.log')&&statSync(join(sdkLogs,n)).mtimeMs>=childStartedAt&&statSync(join(sdkLogs,n)).size>0):[];
  if(profile!=='quiet' && !result.nativeSdkLogs.length) throw new Error('SDK native debug log missing or empty');
  const db=new DatabaseSync(join(reportRoot,'history.sqlite'),{readOnly:true});
  const rows=db.prepare('SELECT full_path,position_key,total_result FROM images ORDER BY sequence_no').all();
  const tools=db.prepare('SELECT tool_name,result,score,overlay_path FROM tool_results ORDER BY tool_result_id').all();db.close();
  result.rows=rows;result.tools=tools;result.savedOverlays=tools.filter(t=>t.overlay_path&&existsSync(t.overlay_path)).length;
  result.success=true;log('VERIFIED',{records:rows.length,tools:tools.length,savedOverlays:result.savedOverlays});
}catch(e){result.success=false;result.error=String(e);log('FAILED',{error:String(e)});process.exitCode=1;}
finally{
  clearInterval(keepAlive);
  if(child.exitCode===null){try{await request('/api/agent/exit',{},3000);}catch{}await sleep(1000);if(child.exitCode===null)child.kill();}
  writeFileSync(join(reportRoot,'report.json'),JSON.stringify(result,null,2));log('REPORT',{path:join(reportRoot,'report.json')});
}
