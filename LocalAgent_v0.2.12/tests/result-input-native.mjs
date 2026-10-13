import {spawn} from 'node:child_process';
import {mkdtempSync,writeFileSync,readFileSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {setTimeout as delay} from 'node:timers/promises';
import {createRequire} from 'node:module';
import {runInNewContext} from 'node:vm';
import assert from 'node:assert/strict';
const sandbox={module:{exports:{}},exports:{},require:createRequire(import.meta.url),Buffer,setTimeout,clearTimeout,setImmediate,clearImmediate};
runInNewContext(readFileSync('assets/jszip.min.js','utf8'),sandbox);const JSZip=sandbox.module.exports;
const worker=resolve(process.argv[2]||'LocalAgent_v0.2.12/Launcher/bin/x64/Release/Workers/8.0/VisionQC.VpdlWorker.exe');
const root=mkdtempSync(join(tmpdir(),'VisionQC-result-input-')),port=17939,base=`http://127.0.0.1:${port}`;
try{await fetch(base+'/api/status',{signal:AbortSignal.timeout(500)});throw Error('Test port already occupied');}catch(error){if(error.message==='Test port already occupied')throw error;}
const request=async(path,body)=>{const data=await(await fetch(base+path,{method:body===undefined?'GET':'POST',headers:{'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(30000)})).json();assert.notEqual(data.ok,false,JSON.stringify(data));return data;};
const finish=async job=>{while(job.running){await delay(20);job=await request('/api/analysis/status',{analysisId:job.analysisId});}assert.equal(job.completed,true,JSON.stringify(job));return job;};
const defs=[{key:'CA_TOP',name:'TCA',aliases:['CA(TOP)']},{key:'CA_BOT',name:'BCA',aliases:['CA(BOT)']}];
const csv=join(root,'top.csv'),csv2=join(root,'bottom.csv'),xlsx=join(root,'top.xlsx');
const header='Date,Time,Cell ID,Position,Total_result,Crack_result,Crack_score\r\n';
writeFileSync(csv,header+'2026-02-03,12:00:00,000001,BCA,NG,NG,0.9000987654321\r\n');
writeFileSync(csv2,header+'2026-02-04,12:00:00,000003,TCA,OK,OK,0.3333333333333\r\n');
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('"','&quot;');
const row=(number,values)=>`<row r="${number}">${values.map((v,i)=>v==null?'':`<c r="${String.fromCharCode(65+i)}${number}" t="inlineStr"><is><t>${esc(v)}</t></is></c>`).join('')}</row>`;
const zip=new JSZip();zip.file('xl/workbook.xml','<workbook xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Results" r:id="rId9"/><sheet name="Ignored" r:id="rId10"/></sheets></workbook>');
zip.file('xl/_rels/workbook.xml.rels','<Relationships><Relationship Id="rId9" Target="/xl/worksheets/sheet9.xml"/><Relationship Id="rId10" Target="worksheets/sheet10.xml"/></Relationships>');
zip.file('xl/sharedStrings.xml','<sst><si><r><t>000</t></r><r><t>002</t></r></si></sst>');
zip.file('xl/worksheets/sheet9.xml',`<worksheet><sheetData>${row(1,['preamble'])}${row(2,header.trim().split(','))}<row r="3"><c r="A3" t="inlineStr"><is><t>2026-02-03</t></is></c><c r="B3" t="inlineStr"><is><t>12:01:02</t></is></c><c r="C3" t="s"><v>0</v></c><c r="D3" t="inlineStr"><is><t>CA(BOT)</t></is></c><c r="E3" t="inlineStr"><is><t>NG</t></is></c><c r="F3" t="inlineStr"><is><t>NG</t></is></c><c r="G3"><f>0.1234567891234</f><v>0.1234567891234</v></c></row>${row(4,['2026-02-03','12:02:03','000004','BCA','OK','OK',null])}</sheetData></worksheet>`);
zip.file('xl/worksheets/sheet10.xml','<worksheet><sheetData>'+row(1,['Cell ID','Total_result'])+row(2,['IGNORED','NG'])+'</sheetData></worksheet>');
writeFileSync(xlsx,await zip.generateAsync({type:'nodebuffer'}));
const child=spawn(worker,[],{cwd:root,windowsHide:true,stdio:'ignore',env:{...process.env,VISIONQC_AGENT_PORT:String(port),VISIONQC_AGENT_HOME:root,VISIONQC_HISTORY_DB_PATH:join(root,'history.sqlite'),COGNEX_VPDL_ROOT:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0',COGNEX_VPDL_DLL_DIR:'C:\\Program Files\\Cognex\\VisionPro Deep Learning\\4.0\\Cognex Deep Learning Studio',VISIONQC_VPDL_API_VERSION:'8.0',VISIONQC_VPDL_PRODUCT_VERSION:'4.0',VISIONQC_VPDL_WORKER_MODE:'exact'}});
const importFiles=async(assignments,files=[...new Set(assignments.map(a=>a.path))])=>finish(await request('/api/analysis/import/start',{filePaths:files,fileAssignments:assignments,options:{positionDefinitions:defs}}));
const dashboard=async job=>(await finish(await request('/api/analysis/dashboard',{analysisId:job.analysisId,positionDefinitions:defs}))).result;
try{
 for(let i=0;i<150;i++){try{await fetch(base+'/api/status');break;}catch{await delay(100);}}
 const assigned=await importFiles([{path:csv,position:'TCA'},{path:xlsx,position:'TCA'},{path:csv2,position:'BCA'}]);
 const model=await dashboard(assigned);assert.equal(model.recordCount,4);
 assert.deepEqual(Object.fromEntries(model.positionSummaries.filter(p=>p.input).map(p=>[p.position,p.total])),{TCA:3,BCA:1});
 const details=await request('/api/analysis/detail',{analysisId:assigned.analysisId,position:'TCA',cellId:'000002',day:'2026-02-03'});
 assert.equal(details.page.records[0].cellId,'000002');assert.equal(details.page.records[0].tools[0].score,.1234567891234);assert.equal(details.page.records[0].captureTimestamp,'2026-02-03T12:01:02');
 assert.equal(details.page.records[0].sourceRowNumber,3);
 const same=await importFiles([{path:csv,position:'TCA'},{path:csv,position:'BCA'},{path:csv,position:'TCA'}]);
 assert.equal((await dashboard(same)).recordCount,2);assert.equal((await dashboard(same)).positionSummaries.filter(p=>p.input).length,2);
 const ordinary=await finish(await request('/api/analysis/import/start',{filePaths:[csv,csv2],options:{defaultPosition:'TCA',positionDefinitions:defs}}));
 assert.deepEqual(Object.fromEntries((await dashboard(ordinary)).positionSummaries.filter(p=>p.input).map(p=>[p.position,p.total])),{TCA:1,BCA:1});
 const map=await finish(await request('/api/analysis/import/start',{filePaths:[csv],filePositions:{[csv]:'TCA'},options:{positionDefinitions:defs}}));assert.equal((await dashboard(map)).positionSummaries.find(p=>p.position==='TCA').total,1);
 const broken=join(root,'broken.xlsx');writeFileSync(broken,'not zip');
 const unchecked=async(path,body)=>(await fetch(base+path,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(body)})).json();
 let bad=await unchecked('/api/analysis/import/start',{filePaths:[broken],fileAssignments:[{path:broken,position:'TCA'}]});while(bad.running){await delay(20);bad=await unchecked('/api/analysis/status',{analysisId:bad.analysisId});}assert.equal(bad.completed,false);assert.ok(bad.error);
 // A failed import does not alter the previously completed analysis.
 await finish(await request('/api/analysis/status',{analysisId:map.analysisId}));
 assert.equal((await dashboard(map)).recordCount,1);
 console.log('PASS selected Position overrides CSV/XLSX metadata, multi-file and same-source assignments, first worksheet, rich shared strings, leading zero IDs, precision/date, unbound history preservation, failed-import recovery. '+root);
}finally{try{await request('/api/agent/exit',{});}catch{}for(let i=0;i<30&&child.exitCode===null;i++)await delay(100);if(child.exitCode===null)child.kill();}
