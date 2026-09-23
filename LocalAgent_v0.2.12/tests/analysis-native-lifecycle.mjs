import {spawn} from 'node:child_process';
import {mkdtempSync,readFileSync,writeFileSync,mkdirSync,readdirSync,copyFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {setTimeout as delay} from 'node:timers/promises';

const [worker,fixture,mode='candidate',layout='parallel']=process.argv.slice(2);
if(!worker||!fixture)throw new Error('worker and fixture paths required');
const root=mkdtempSync(join(tmpdir(),`VisionQC-481-${mode}-`)),port=17931,base=`http://127.0.0.1:${port}`;
const config=JSON.parse(readFileSync(join(fixture,'request.json'),'utf8'));
const input=join(root,'input');mkdirSync(input);mkdirSync(join(root,'output'));mkdirSync(join(root,'export'));
const source=config.positions[0].greenImageRoot;
const images=readdirSync(source).filter(n=>/\.(jpg|png|bmp|jpeg)$/i.test(n)).sort().slice(0,3);
if(images.length!==3)throw new Error('Three fixture images required');
for(const name of images)copyFileSync(join(source,name),join(input,name));
config.positions.forEach(p=>{p.greenImageRoot=input;p.greenImageRoots=[input];});
config.green.keywordMode=false;config.green.keywordInputRoots=[];config.green.cellIdCsvPath='';
config.green.detailedDiagnostics=false;config.green.heatmapImageSave=false;
if(layout==='single'){config.positions=config.positions.slice(0,1);config.parallelPositions=false;}
config.outputRoot=join(root,'output');config.csvMaxRows=2;config.csvSplitByDate=true;config.agentAnalysis=mode==='candidate';
config.webVersion='4.8.7';
const expected=images.length*config.positions.filter(p=>p.enabled!==false).length;
let child;const report={root,mode,expected,simulations:[]};
const request=async(path,body,timeout=30000)=>{
 const response=await fetch(base+path,{method:body===undefined?'GET':'POST',headers:body===undefined?{}:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(timeout)});
 const data=await response.json();if(!response.ok||data.ok===false)throw new Error(path+': '+JSON.stringify(data));return data;
};
const complete=async(start)=>{let status=start;while(status.running){await delay(100);status=await request('/api/analysis/status',{analysisId:start.analysisId});}if(!status.completed)throw new Error(JSON.stringify(status));return status;};
const start=async()=>{
 try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw new Error('Isolated test port already in use');}catch(e){if(e.message==='Isolated test port already in use')throw e;}
 child=spawn(resolve(worker),['--worker'],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
 for(let n=0;n<240;n++){if(child.exitCode!==null)throw new Error('Worker exited '+child.exitCode);try{await request('/api/status',undefined,1000);return;}catch{}await delay(500);}throw new Error('Worker startup timeout');
};
const stop=async()=>{if(child?.exitCode===null){try{await request('/api/agent/exit',{},10000);}catch{}for(let i=0;i<30&&child.exitCode===null;i++)await delay(200);if(child.exitCode===null)child.kill();}};
try{
 await start();writeFileSync(join(root,'request.json'),JSON.stringify(config,null,2));
 for(let run=0;run<(layout==='single'?1:3);run++){
   await request('/api/runtime/preload',config,240000);
   const launched=await request('/api/simulation/start',config);
   let state=launched.state;
   while(state.running){await delay(100);state=(await request('/api/status')).state;}
   if(state.error||state.processed!==expected)throw new Error('Inference count/error '+JSON.stringify(state));
   report.simulations.push({processed:state.processed,elapsedSeconds:state.elapsedSeconds,resultCsv:state.resultCsv});
   if(mode==='candidate'&&readdirSync(join(root,'output'),{recursive:true}).some(name=>name.endsWith('.parts.txt')))throw new Error('Completed simulation left CSV inventory');
   const rows=await request('/api/simulation/results',{runId:state.simulationRunId,afterImageId:0,pageSize:1000});
   if(rows.records.length!==expected)throw new Error('Raw result count mismatch');
   report.scores=rows.records.map(r=>({cell:r.CellId,position:r.Position,tools:r.Tools}));
   if(mode==='candidate'){
     const bound=await complete(await request('/api/simulation/analysis',{runId:state.simulationRunId}));
     const summary=await complete(await request('/api/analysis/dashboard',{analysisId:bound.analysisId}));
     if(summary.result.rawRowCount!==expected)throw new Error('Native projection lost inference rows');
     report.analysisId=bound.analysisId;
   }
   await request('/api/simulation/history-decision',{runId:state.simulationRunId,decision:'discard'});
 }
 if(mode==='candidate'){
   const exported=await complete(await request('/api/analysis/export',{analysisId:report.analysisId,outputDirectory:join(root,'export'),maxRows:2,splitByDate:true}));
   if(exported.result.count!==expected||exported.result.files.length<Math.ceil(expected/2))throw new Error('Partition export count mismatch');
   for(const path of exported.result.files){const lines=readFileSync(path,'utf8').trim().split(/\r?\n/);if(!lines[0].startsWith('\ufeffDate,Time,')&&!lines[0].startsWith('Date,Time,'))throw new Error('Split Date/Time missing');if(lines.length>3)throw new Error('Partition cap exceeded');}
   const imported=await complete(await request('/api/analysis/import/start',{filePaths:exported.result.files}));report.analysisId=imported.analysisId;
   const loaded=await complete(await request('/api/analysis/dashboard',{analysisId:report.analysisId}));
   if(loaded.result.rawRowCount!==expected)throw new Error('CSV reimport lost rows');
   await complete(await request('/api/analysis/save-history',{analysisId:report.analysisId}));
   await complete(await request('/api/analysis/save-history',{analysisId:report.analysisId}));
   const history=await request('/api/history/search',{pageSize:100});
   if(history.totalCount!==expected)throw new Error('Explicit history save duplicated rows: '+JSON.stringify(history));
   await stop();await start();
   await complete(await request('/api/analysis/status',{analysisId:report.analysisId}));
   const restored=await complete(await request('/api/analysis/dashboard',{analysisId:report.analysisId}));
   if(restored.result.rawRowCount!==expected)throw new Error('Restart reference lost rows');
   report.restartVerified=true;report.csvFiles=exported.result.files;
 }
 report.success=true;
}catch(error){report.success=false;report.error=String(error);process.exitCode=1;}
finally{await stop();writeFileSync(join(root,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));}
