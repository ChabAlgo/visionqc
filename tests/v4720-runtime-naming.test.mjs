import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const web=read('visionqc-extension.js'), server=read('LocalAgent_v0.2.12/AgentServer.cs');
const helpers=new Function(web.slice(web.indexOf('const defaultNamingProfile'),web.indexOf('const initialNamingProfile'))+';return {defaultNamingProfile,sanitizeNamingProfile};')();
test('combined timestamp defaults and legacy position migration',()=>{
  assert.equal(helpers.defaultNamingProfile().dateTime.mode,'compact');
  assert.equal(helpers.defaultNamingProfile().dateTime.tokenIndex,3);
  const old=helpers.defaultNamingProfile(); delete old.dateTime; old.date.mode='token'; old.time.mode='token'; old.time.tokenIndex=6;
  const migrated=helpers.sanitizeNamingProfile(old);
  assert.equal(migrated.dateTime.mode,'legacy'); assert.equal(migrated.time.tokenIndex,6);
  assert.equal(helpers.sanitizeNamingProfile({...old,dateTime:{mode:'auto'}}).dateTime.mode,'compact');
  assert.equal(helpers.sanitizeNamingProfile({...old,dateTime:{mode:'token',tokenIndex:3}}).dateTime.tokenIndex,3);
});
test('startup and repeated status checks never initialize a GPU control',()=>{
  const start=server.slice(server.indexOf('public void RunUntilExit'),server.indexOf('private static string ResolveHistoryDatabasePath'));
  assert.doesNotMatch(start,/RuntimeCheck\(/);
  const check=server.slice(server.indexOf('private object RuntimeCheck'),server.indexOf('private RuntimePreloadResponse PreloadRuntime'));
  assert.doesNotMatch(check,/EnsureInspectionControl|new LocalRuntime|Dispose\(|InitializeComputeDevices/);
  assert.match(check,/licenseVerified = false/); assert.match(web,/data.message \|\|/);
});
test('explicit workspace inspection and real preload retain GPU support for high-detail tools',()=>{
  const inspect=server.slice(server.indexOf('private WorkspaceInspectionResponse InspectWorkspace'),server.indexOf('private LocalRuntime.Control EnsureInspectionControl'));
  assert.match(inspect,/EnsureInspectionControl\(useGpu, mode, gpuList, false\)/);
  assert.match(inspect,/EnsureInspectionControl\(useGpu, mode, gpuList, fallbackDeferred\)/);
  const preload=server.slice(server.indexOf('private RuntimePreloadResponse PreloadRuntime'),server.indexOf('private WorkspaceInspectionResponse InspectWorkspace'));
  assert.match(preload,/GreenRuntimeFactory.Create\(gpuMode, gpuList/);
  assert.match(preload,/RememberPreloadedRuntimeLocked/);
});
