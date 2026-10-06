import {test,expect} from '@playwright/test';
test('exclusion setting validates, persists old names and passes scan exclusions to simulation',async({page})=>{
 await page.addInitScript(()=>localStorage.setItem('visionqc-v43-active-page','settings'));
 await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 const input=page.locator('#vq4810-exclusion-folder');await expect(input).toHaveValue('Delet');
 await input.fill('Discard');await input.press('Tab');await input.fill('Ignore');await input.press('Tab');
 const saved=await page.evaluate(()=>JSON.parse(localStorage.getItem('visionqc-exclusion-folders')));expect(saved.names).toEqual(['Delet','Discard','Ignore']);
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.simulationRequestSnapshot().exclusionFolderNames)).toEqual(saved.names);
 page.on('dialog',d=>d.accept());await input.fill('../outside');await input.press('Tab');await expect(input).toHaveValue('Ignore');
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());await expect(page.locator('#vq4810-exclusion-folder')).toHaveValue('Ignore');
 const snapshot=await page.evaluate(async()=>{
  const root=await navigator.storage.getDirectory();const dir=await root.getDirectoryHandle('images',{create:true});
  for(const name of ['delet','Discard','IGNORE','normal']){const child=await dir.getDirectoryHandle(name,{create:true});const nested=await child.getDirectoryHandle('nested',{create:true});const file=await nested.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});const w=await file.createWritable();await w.write('test');await w.close();}
  window.showDirectoryPicker=async()=>dir;await window.__VISIONQC_DEBUG__.chooseNgPositionFolder('AN(TOP)');return window.__VISIONQC_DEBUG__.analysisInputSnapshot();
 });expect(snapshot.images).toHaveLength(1);
});
