import {test,expect} from '@playwright/test';
async function open(page){await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());}
const rows=[1,2,3].map(i=>({cellId:`P163GG22M210000${i}`,position:'AN(TOP)',captureTimestamp:'2026-02-03T08:00:00',totalResult:'OK',tools:{Crack:{tool:'Crack',result:'OK',score:.8}}}));
test('multiple NG folders, repeated exclusions, next image and refreshed counts survive reload',async({page})=>{
 await open(page);await page.evaluate(rows=>window.__VISIONQC_DEBUG__.seedRows(rows),rows);
 await page.evaluate(async()=>{
  const root=await navigator.storage.getDirectory();
  for(let folder=0;folder<2;folder++){
   const dir=await root.getDirectoryHandle('actual-'+folder,{create:true});
   for(const i of folder===0?[1,2]:[3]){const h=await dir.getFileHandle(`20260203080000_P163GG22M210000${i}.png`,{create:true});const w=await h.createWritable();await w.write(Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII='),c=>c.charCodeAt(0)));await w.close();}
   window.showDirectoryPicker=async()=>dir;await window.__VISIONQC_DEBUG__.chooseNgPositionFolder('AN(TOP)');
  }
  window.__VISIONQC_DEBUG__.openFirstMiss();
 });
 page.on('dialog',dialog=>dialog.accept());
 const snapshot=()=>page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot());
 expect((await snapshot()).misses).toHaveLength(3);
 await page.keyboard.press('ArrowLeft');await expect(page.locator('.vq43-boundary')).toHaveText('첫 이미지입니다.');
 for(const count of [2,1]){
  await page.locator('[data-vq-action="modal-move-delet"]').click();
  await expect.poll(async()=>(await snapshot()).misses.length).toBe(count);
  await expect(page.locator('#vq43-modal')).toHaveClass(/open/);
  expect((await snapshot()).images).toHaveLength(count);
  await expect(page.locator('.vq43-miss-row:not(.head)')).toHaveCount(count);
 }
 await page.keyboard.press('ArrowRight');await expect(page.locator('.vq43-boundary')).toHaveText('마지막 이미지입니다.');
 await page.evaluate(()=>window.__VISIONQC_DEBUG__.saveAnalysisSnapshot());await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 expect((await snapshot()).misses).toHaveLength(1);expect((await snapshot()).images).toHaveLength(1);
 await page.locator('[data-vq-action="open-miss"]').first().click();await expect(page.locator('#vq43-modal')).toHaveClass(/open/);
});
test('Cell ID CSV preserves IDs, deduplicates, matches each date and restores input without source files',async({page})=>{
 await open(page);await page.evaluate(rows=>window.__VISIONQC_DEBUG__.seedRows([...rows,{...rows[0],captureTimestamp:'2026-02-04T08:00:00'}]),rows);
 const count=await page.evaluate(()=>window.__VISIONQC_DEBUG__.importNgCellCsv(new File(['Cell ID,Note\r\nP163GG22M2100001,a\r\nP163GG22M2100001,b\r\n000123,c\r\n'],'actual.csv',{type:'text/csv'}),'AN(TOP)'));
 expect(count).toBe(2);
 let snap=await page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot());expect(snap.misses).toHaveLength(2);expect(snap.images.map(i=>i.cellId)).toContain('000123');
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());snap=await page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot());expect(snap.misses).toHaveLength(2);expect(snap.summaries.find(p=>p.position==='AN(TOP)').total).toBe(4);
 const error=await page.evaluate(async()=>{try{await window.__VISIONQC_DEBUG__.importNgCellCsv(new File(['Wrong\n123'],'bad.csv'),'AN(TOP)');return '';}catch(e){return e.message;}});expect(error).toContain('Cell ID');
});
