import {test,expect} from '@playwright/test';
import {readFileSync} from 'node:fs';
const agentVersion=JSON.parse(readFileSync(new URL('../RELEASE_MANIFEST.json',import.meta.url))).agentVersion;
async function open(page){
 await page.route('http://127.0.0.1:*/api/**',route=>route.abort());
 await page.goto('/index.html?vqDebug=1&browserRegression=1');
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
}

test('NG registrations preserve same-name different roots, refresh new cells, restore and remove one root only',async({page})=>{
 await open(page);
 const before=await page.evaluate(async()=>{
  const d=window.__VISIONQC_DEBUG__,root=await navigator.storage.getDirectory();
  const paths=[];
  for(const name of ['one','two']){const p=await root.getDirectoryHandle(name,{create:true});const dir=await p.getDirectoryHandle('SameName',{create:true});paths.push(dir);
   const h=await dir.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});const w=await h.createWritable();await w.write('fixture');await w.close();}
  for(const dir of paths){window.showDirectoryPicker=async()=>dir;await d.chooseNgPositionFolder('AN(TOP)');}
  window.showDirectoryPicker=async()=>paths[0];await d.chooseNgPositionFolder('AN(TOP)');
  const h=await paths[1].getFileHandle('20260203080001_P163GG22M2100002.png',{create:true});const w=await h.createWritable();await w.write('fixture');await w.close();
  // This prior CSV contains only the old ID; refresh must still index the newly added ID.
  await d.seedCsv('Cell ID,Total_result,Crack_result,Crack_score\nP163GG22M2100001,NG,NG,0.8');
  await d.refreshActualNgFolders();d.setPage('settings');await d.saveAnalysisSnapshot();
  return {sources:d.ngSources(),images:d.analysisInputSnapshot().images};
 });
 expect(before.sources).toHaveLength(2);expect(before.images).toHaveLength(3);
 expect(before.images.some(x=>x.cellId==='P163GG22M2100002')).toBe(true);
 await expect(page.locator('.vq4816-ng-source')).toHaveCount(2);
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 await expect(page.locator('.vq4816-ng-source')).toHaveCount(2);
 const removeKey=before.images.find(image=>before.images.filter(other=>other.rootHandleKey===image.rootHandleKey).length===1).rootHandleKey;
 await page.locator(`[data-ng-source="${removeKey}"]`).click();
 await expect(page.locator('.vq4816-ng-source')).toHaveCount(1);
 const after=await page.evaluate(async()=>{
  const d=window.__VISIONQC_DEBUG__,root=await navigator.storage.getDirectory();const p=await root.getDirectoryHandle('one');const dir=await p.getDirectoryHandle('SameName');
  return {images:d.analysisInputSnapshot().images,originalStillPresent:!!await dir.getFileHandle('20260203080000_P163GG22M2100001.png')};
 });expect(after.images).toHaveLength(2);expect(after.originalStillPresent).toBe(true);
});

test('failed NG refresh leaves all previous registrations and labels intact',async({page})=>{
 await open(page);
 const result=await page.evaluate(async()=>{
  const d=window.__VISIONQC_DEBUG__,root=await navigator.storage.getDirectory();const dir=await root.getDirectoryHandle('unavailable',{create:true});
  const h=await dir.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});const w=await h.createWritable();await w.write('fixture');await w.close();
  window.showDirectoryPicker=async()=>dir;await d.chooseNgPositionFolder('AN(TOP)');
  const before=d.analysisInputSnapshot().images;await root.removeEntry('unavailable',{recursive:true});
  let failed=false;try{await d.refreshActualNgFolders();}catch{failed=true;}
  return {failed,before,after:d.analysisInputSnapshot().images,sources:d.ngSources()};
 });expect(result.failed).toBe(true);expect(result.after).toEqual(result.before);expect(result.sources).toHaveLength(1);
});

test('viewer previous and next controls stay separate at desktop and small widths with keyboard navigation',async({page})=>{
 await open(page);await page.evaluate(()=>window.__VISIONQC_DEBUG__.openMissSequenceRegression());
 for(const width of [1920,800]){
  await page.setViewportSize({width,height:900});
  const left=await page.locator('#vq43-modal [data-vq-action="modal-prev"]').boundingBox(),right=await page.locator('#vq43-modal [data-vq-action="modal-next"]').boundingBox();
  expect(right.x-left.x-left.width).toBeGreaterThan(24);expect(Math.abs(right.y-left.y)).toBeLessThan(2);
 }
 await page.keyboard.press('ArrowRight');await expect.poll(()=>page.evaluate(()=>window.__VISIONQC_DEBUG__.modalSequenceSnapshot().index)).toBe(1);
});

test('simulation folder addition retains previous paths, deduplicates and preserves cancel',async({page})=>{
 const choices=[{ok:true,paths:['C:\\A','D:\\B']},{ok:true,paths:['E:\\C','c:\\a']},{ok:false,path:''}];let picks=0;
 await page.route('http://127.0.0.1:*/api/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  const json=path==='/api/status'?{ok:true,agentVersion,vpdlAvailable:true,instanceId:'test',gpuDeviceIndices:[0,1],state:{running:false}}:path==='/api/pick/start'?choices[picks++]:{ok:true};
  await route.fulfill({json});
 });
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.openSimulation());
 await page.locator('[data-vq-action="simulation-mode"][data-vq-mode="green"]').click();
 const row=page.locator('.vq43-sim-path').filter({has:page.locator('input[data-sim-field="greenImageRoot"]')}).first();
 await expect(row.locator('button').first()).toBeEnabled();await row.locator('button').first().click();
 await expect(row.locator('input')).toHaveValue('C:\\A; D:\\B');
 await row.locator('[data-sim-append="1"]').click();await expect(row.locator('input')).toHaveValue('C:\\A; D:\\B; E:\\C');
 await row.locator('[data-sim-append="1"]').click();await expect(row.locator('input')).toHaveValue('C:\\A; D:\\B; E:\\C');
 const request=await page.evaluate(()=>window.__VISIONQC_DEBUG__.simulationRequestSnapshot());
 expect(request.positions[0].greenImageRoots).toEqual(['C:\\A','D:\\B','E:\\C']);
 expect(picks).toBe(3);
});
