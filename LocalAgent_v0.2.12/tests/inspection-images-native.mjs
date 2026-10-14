import {spawn} from 'node:child_process';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,readdirSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {setTimeout as delay} from 'node:timers/promises';
import assert from 'node:assert/strict';
const worker=resolve(process.argv[2]||'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe');
const root=mkdtempSync(join(tmpdir(),'VisionQC-inspection-images-')),port=17942,base=`http://127.0.0.1:${port}`;
try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw Error('Test port already occupied');}catch(error){if(error.message==='Test port already occupied')throw error;}
const request=async(path,body)=>{const data=await(await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(30000)})).json();assert.notEqual(data.ok,false,JSON.stringify(data));return data;};
const finish=async job=>{while(job.running){await delay(20);job=await request('/api/analysis/status',{analysisId:job.analysisId});}assert.equal(job.completed,true,JSON.stringify(job));return job;};
const source=join(root,'mixed');mkdirSync(source);const other=join(root,'different-root');mkdirSync(other);
const make=(name,folder=source)=>{const path=join(folder,name);writeFileSync(path,'bytes:'+path);return path;};
const inspected=make('CELL01_input_RANDOM.jpg'),otherInspection=make('CELL01_other-inspection.png'),below=make('CELL01_low.tif'),noScore=make('CELL01_unknown-score.jpg'),miss=make('CELL02_source.jpeg'),otherDate=make('CELL01_input_RANDOM.jpg',other);
const preview=make('CELL01_HTMT.png'),processed=make('CELL01_BLUTOL.jpg'),extra=make('CELL01_any-other-suffix.bmp');
const missing=join(source,'missing.jpg');
const csv=join(root,'input.csv'),header='ImagePath,Date,Time,Cell ID,Position,Total_result,Crack_result,Crack_score,Other_result,Other_score,FullPath,ProcessedPath';
const rows=[
 [preview,'2026-02-03','12:00:00','CELL01','AN(TOP)','NG','NG','.9','OK','.1',inspected,processed],
 [preview,'2026-02-03','12:00:01','CELL01','AN(TOP)','NG','OK','.2','NG','.99',otherInspection,processed],
 [extra,'2026-02-03','12:00:02','CELL01','AN(TOP)','NG','NG','.6','OK','.1',below,processed],
 [extra,'2026-02-03','12:00:03','CELL01','AN(TOP)','NG','NG','','OK','.1',noScore,processed],
 [preview,'2026-02-03','12:00:04','MISSING','AN(TOP)','NG','NG','.95','OK','.1',missing,processed],
 [preview,'2026-02-03','12:00:05','CELL02','AN(TOP)','OK','OK','.2','OK','.1',miss,processed],
 [preview,'2026-02-04','12:00:00','CELL01','AN(TOP)','NG','NG','.99','OK','.1',otherDate,processed]
];
writeFileSync(csv,[header,...rows.map(row=>row.join(','))].join('\r\n'));
const child=spawn(worker,[],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
try{
 for(let i=0;i<100;i++){try{await request('/api/status');break;}catch{await delay(100);}}
 const imported=await finish(await request('/api/analysis/import/start',{filePaths:[csv]})),analysisId=imported.analysisId;
 await finish(await request('/api/analysis/thresholds',{analysisId,thresholds:[{position:'AN(TOP)',tool:'Crack',value:.8}]}));
 const exportCopy=async(kind,options={})=>{const output=join(root,kind+'-'+Math.random().toString(16).slice(2));mkdirSync(output);const job=await finish(await request('/api/analysis/export',{analysisId,kind,copyImages:true,outputDirectory:output,...options}));return {output,result:job.result};};
 const files=path=>readdirSync(path,{withFileTypes:true}).flatMap(e=>e.isDirectory()?files(join(path,e.name)):e.name.endsWith('.csv')?[]:[join(path,e.name)]);
 const check=(copied,expected)=>{const actual=files(copied.output);assert.equal(actual.length,expected.length);assert.deepEqual(actual.map(p=>readFileSync(p,'utf8')).sort(),expected.map(p=>readFileSync(p,'utf8')).sort());};
 const tool=await exportCopy('tool-ng',{position:'AN(TOP)',tool:'Crack',date:'2026-02-03'});
 assert.equal(tool.result.count,2);assert.equal(tool.result.images.total,3);assert.equal(tool.result.images.copied,2);assert.equal(tool.result.images.failed,1);check(tool,[inspected,noScore]);
 const detail=await request('/api/analysis/detail',{analysisId,day:'2026-02-03',cellId:'CELL01',position:'AN(TOP)'});assert.equal(detail.page.records[0].fullPath,inspected);assert.equal(detail.page.records[0].processedPath,processed);
 const batchId='11111111111111111111111111111111';await request('/api/analysis/actual-ng',{analysisId,batchId,action:'begin'});await request('/api/analysis/actual-ng',{analysisId,batchId,action:'append',actualNg:[{day:'2026-02-03',cell:'CELL02',position:'AN(TOP)',path:preview}]});await request('/api/analysis/actual-ng',{analysisId,batchId,action:'commit'});
 const misses=await exportCopy('misses',{position:'AN(TOP)',date:'2026-02-03'});assert.equal(misses.result.count,1);check(misses,[miss]);
 const all=await exportCopy('all');assert.equal(all.result.count,7);assert.equal(all.result.images.failed,1);check(all,[inspected,otherInspection,below,noScore,miss,otherDate]);
 const selected=await exportCopy('selection',{positions:['AN(TOP)'],date:'2026-02-03'});check(selected,[inspected,otherInspection,below,noScore,miss]);
 // Missing canonical FullPath must fail, never fall back to existing preview/crop/same-Cell siblings.
 assert.ok(readFileSync(all.result.images.report,'utf8').includes(missing));
 console.log('PASS exact logged FullPath only; Tool source observation and threshold; no preview/crop/actual-folder expansion; arbitrary suffixes and extensions; missing source cannot substitute sibling; date, misses and all copy preserve bytes. '+root);
}finally{try{await request('/api/agent/exit',{});}catch{}for(let i=0;i<30&&child.exitCode===null;i++)await delay(100);if(child.exitCode===null)child.kill();}
