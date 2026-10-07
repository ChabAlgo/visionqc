import {test,expect} from '@playwright/test';
async function open(page){await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());}
test('explicit exclusions add/delete/select and migrate without hidden historical names',async({page})=>{
 await page.addInitScript(()=>{localStorage.setItem('visionqc-v43-active-page','settings');if(!localStorage.getItem('visionqc-exclusion-folders'))localStorage.setItem('visionqc-exclusion-folders',JSON.stringify({name:'Discard',names:['Delet','Old','Discard']}));});
 await open(page);
 await expect(page.locator('[data-exclusion-delete]')).toHaveCount(1);
 await expect(page.locator('[data-exclusion-delete]')).toHaveAttribute('data-exclusion-delete','Discard');
 const add=async name=>{await page.locator('#vq4810-exclusion-folder').fill(name);await page.locator('#vq4812-exclusion-add').click();};
 await add('Ignore');await page.locator('[data-exclusion-destination="Ignore"]').check();
 await page.locator('[data-exclusion-delete="Discard"]').click();
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.simulationRequestSnapshot().exclusionFolderNames)).toEqual(['Ignore']);
 page.on('dialog',d=>d.accept());await add('../outside');await add('IGNORE');await expect(page.locator('[data-exclusion-delete]')).toHaveCount(1);
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 await expect(page.locator('[data-exclusion-destination="Ignore"]')).toBeChecked();
 const snapshot=await page.evaluate(async()=>{
  const root=await navigator.storage.getDirectory();const dir=await root.getDirectoryHandle('images',{create:true});
  for(const name of ['delet','Discard','IGNORE','normal']){const child=await dir.getDirectoryHandle(name,{create:true});const nested=await child.getDirectoryHandle('nested',{create:true});const file=await nested.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});const w=await file.createWritable();await w.write('test');await w.close();}
  window.showDirectoryPicker=async()=>dir;await window.__VISIONQC_DEBUG__.chooseNgPositionFolder('AN(TOP)');return window.__VISIONQC_DEBUG__.analysisInputSnapshot();
 });expect(snapshot.images).toHaveLength(3);
 await page.locator('[data-exclusion-delete="Ignore"]').click();
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.simulationRequestSnapshot().exclusionFolderNames)).toEqual([]);
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());await expect(page.locator('[data-exclusion-delete]')).toHaveCount(0);
});
test('settings columns preserve requested order and fit desktop',async({page})=>{
 await page.addInitScript(()=>localStorage.setItem('visionqc-v43-active-page','settings'));await open(page);
 expect(await page.locator('.vq4812-settings-left h3').allTextContents()).toEqual(['분석 준비 상태','1. Position 구성','2. 실제 최종 NG 이미지 경로','3. Position별 시뮬레이션 결과 파일']);
 const left=await page.locator('.vq4812-settings-left').boundingBox(),right=await page.locator('.vq4812-settings-right').boundingBox();expect(right.x).toBeGreaterThan(left.x+left.width);expect(left.width+right.width).toBeGreaterThan(1600);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.screenshot({path:'test-results/v4812-settings.png',fullPage:true});
 await page.locator('#vq4810-exclusion-folder').scrollIntoViewIfNeeded();await page.screenshot({path:'test-results/v4812-settings-bottom.png',fullPage:true});
});

test('CSV and image copy actions stay adjacent and right aligned',async({page})=>{
 await open(page);await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedRows([{cellId:'CELL1',position:'AN(TOP)',captureTimestamp:'2026-02-03T08:00:00',totalResult:'NG',tools:{Crack:{tool:'Crack',result:'NG',score:.8}}}]));
 const adjacent=async(first,last)=>{const a=await page.locator(first).boundingBox(),b=await page.locator(last).boundingBox();expect(Math.abs(a.y-b.y)).toBeLessThan(4);expect(b.x-a.x-a.width).toBeGreaterThanOrEqual(0);expect(b.x-a.x-a.width).toBeLessThan(9);};
 await adjacent('[data-vq-action="download-chart-csv"]','[data-vq-action="copy-chart-images"]');
 await adjacent('[data-vq-action="download-misses"]','[data-vq-action="copy-miss-images"]');
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.setPage('analysis'));
 await adjacent('[data-vq-action="download-score-filter"]','[data-vq-action="copy-score-images"]');
 await page.screenshot({path:'test-results/v4812-score.png',fullPage:true});
});
