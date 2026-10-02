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
  window.showDirectoryPicker=async()=>root;
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
 const moved=await page.evaluate(async()=>{
  const root=await navigator.storage.getDirectory();
  const deleted=await root.getDirectoryHandle('DELET');
  const source=await root.getDirectoryHandle('actual-0');
  const target=await deleted.getDirectoryHandle('actual-0');
  const names=[];for await(const [name] of target.entries())names.push(name);
  let sourceCount=0;for await(const [,entry] of source.entries())if(entry.kind==='file')sourceCount++;
  return {names,sourceCount};
 });
 expect(moved.names).toHaveLength(2);expect(moved.sourceCount).toBe(0);
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

test('same named roots at different locations append independently, refresh without duplicates and replace explicitly',async({page})=>{
 await open(page);await page.evaluate(rows=>window.__VISIONQC_DEBUG__.seedRows(rows),rows);
 const read=()=>page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot());
 await page.evaluate(async()=>{
  const storage=await navigator.storage.getDirectory();
  window.testRoots=[];
  for(const name of ['parent-a','parent-b']){
   const parent=await storage.getDirectoryHandle(name,{create:true});
   const root=await parent.getDirectoryHandle('same-name',{create:true});
   const position=await root.getDirectoryHandle('AN(TOP)',{create:true});
   const file=await position.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});
   const writer=await file.createWritable();await writer.write('fixture');await writer.close();window.testRoots.push(root);
  }
  for(const root of window.testRoots){window.showDirectoryPicker=async()=>root;await window.__VISIONQC_DEBUG__.chooseNgFolder(true);}
 });
 let snap=await read();expect(snap.images).toHaveLength(2);expect(new Set(snap.images.map(i=>i.key)).size).toBe(2);
 await page.evaluate(async()=>{window.showDirectoryPicker=async()=>window.testRoots[0];await window.__VISIONQC_DEBUG__.chooseNgFolder(true);await window.__VISIONQC_DEBUG__.saveAnalysisSnapshot();});
 expect((await read()).images).toHaveLength(2);
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());expect((await read()).images).toHaveLength(2);
 await page.evaluate(async()=>{const storage=await navigator.storage.getDirectory();const parent=await storage.getDirectoryHandle('parent-a');const root=await parent.getDirectoryHandle('same-name');window.showDirectoryPicker=async()=>root;await window.__VISIONQC_DEBUG__.chooseNgFolder(false);});
 expect((await read()).images).toHaveLength(1);
});

test('configured position order preserves unknown positions after configured ones',async({page})=>{
 await open(page);
 const ordered=await page.evaluate(()=>window.__VISIONQC_DEBUG__.configuredPositionOrder(['AN(BOT)','AN(TOP)','CA(BOT)','CA(TOP)','OTHER'],x=>x));
 expect(ordered).toEqual(['CA(TOP)','AN(TOP)','CA(BOT)','AN(BOT)','OTHER']);
});

test('wrong parent and cancelled parent picker preserve image and dashboard',async({page})=>{
 await open(page);await page.evaluate(rows=>window.__VISIONQC_DEBUG__.seedRows(rows),rows);
 await page.evaluate(async()=>{
  const storage=await navigator.storage.getDirectory();const root=await storage.getDirectoryHandle('actual-source',{create:true});
  const file=await root.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});const w=await file.createWritable();await w.write('source');await w.close();
  window.showDirectoryPicker=async()=>root;await window.__VISIONQC_DEBUG__.chooseNgPositionFolder('AN(TOP)');window.__VISIONQC_DEBUG__.openFirstMiss();
 });
 page.on('dialog',dialog=>dialog.accept());
 await page.locator('[data-vq-action="modal-move-delet"]').click();
 await expect(page.locator('#vq43-toast')).toContainText('상위 폴더');
 expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot())).images).toHaveLength(1);
 await page.evaluate(()=>{window.showDirectoryPicker=async()=>{throw new DOMException('cancelled','AbortError');};});
 await page.locator('[data-vq-action="modal-move-delet"]').click();
 await expect(page.locator('#vq43-toast')).toContainText('cancelled');
 const bytes=await page.evaluate(async()=>{const root=await (await navigator.storage.getDirectory()).getDirectoryHandle('actual-source');return (await (await root.getFileHandle('20260203080000_P163GG22M2100001.png')).getFile()).text();});
 expect(bytes).toBe('source');
});
