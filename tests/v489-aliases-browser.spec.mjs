import {test,expect} from '@playwright/test';
async function open(page){await page.addInitScript(()=>localStorage.setItem('visionqc-v43-active-page','settings'));await page.goto('/index.html?vqDebug=1&browserRegression=1');await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());}
test('rename retains stable identity and old alias, edits persist and UI adds aliases with new position',async({page})=>{
 await open(page);
 await page.locator('[data-position-name-key="CA_TOP"]').fill('TCA');await page.locator('[data-position-name-key="CA_TOP"]').press('Tab');
 await expect(page.locator('[data-position-alias-key="CA_TOP"]')).toHaveValue('CA(TOP)');
 await page.locator('[data-position-alias-key="CA_TOP"]').fill('CA(TOP), CA_TOP');await page.locator('[data-position-alias-key="CA_TOP"]').press('Tab');
 await page.locator('#vq43-new-position-name').fill('SIDE');await page.locator('#vq43-new-position-aliases').fill('CAM5, SIDE_CAM');await page.locator('[data-vq-action="position-add"]').click();
 const before=await page.evaluate(()=>window.__VISIONQC_DEBUG__.positionDefinitions());expect(before.find(p=>p.key==='CA_TOP')).toEqual({key:'CA_TOP',name:'TCA',aliases:['CA(TOP)','CA_TOP']});expect(before.find(p=>p.name==='SIDE').aliases).toEqual(['CAM5','SIDE_CAM']);
 await page.reload();await page.waitForFunction(()=>window.__VISIONQC_DEBUG__?.analysisRestoreReady());
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.normalizePosition('ca_top'))).toBe('TCA');
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.normalizePosition('SIDE_CAM'))).toBe('SIDE');
 await page.screenshot({path:'test-results/v489-alias-settings.png',fullPage:true});
});
test('cross-position canonical and normalized alias duplicates are rejected in a visible dialog',async({page})=>{
 await open(page);const messages=[];page.on('dialog',async d=>{messages.push(d.message());await d.accept();});
 await page.locator('[data-position-alias-key="AN_TOP"]').fill('CA_TOP');await page.locator('[data-position-alias-key="AN_TOP"]').press('Tab');
 expect(messages.join(' ')).toContain('중복');expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.positionDefinitions().find(d=>d.key==='AN_TOP').aliases)).toEqual([]);
 await page.locator('#vq43-new-position-name').fill('NEW');await page.locator('#vq43-new-position-aliases').fill('CA(TOP)');await page.locator('[data-vq-action="position-add"]').click();
 expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.positionDefinitions().length)).toBe(4);
});
test('Unicode aliases retain distinct identities and invalid path names are rejected',async({page})=>{
 await open(page);page.on('dialog',d=>d.accept());
 const result=await page.evaluate(()=>{const d=window.__VISIONQC_DEBUG__;d.addPositionDefinition('상부',['윗면']);d.addPositionDefinition('하부',['아랫면']);const invalid=d.addPositionDefinition('BAD/NAME',[]);return [d.normalizePosition('윗면'),d.normalizePosition('아랫면'),invalid];});expect(result).toEqual(['상부','하부',null]);
});
test('NG root aliases match representative names and conflicting folder paths preserve prior data',async({page})=>{
 await open(page);
 const result=await page.evaluate(async()=>{
  const d=window.__VISIONQC_DEBUG__;await d.renameCustomPosition('CA_TOP','TCA');d.seedRows([{cellId:'P163GG22M2100001',position:'TCA',totalResult:'OK',tools:{Crack:{tool:'Crack',result:'OK',score:.8}}}]);
  const root=await (await navigator.storage.getDirectory()).getDirectoryHandle('alias-root',{create:true});const pos=await root.getDirectoryHandle('CA(TOP)',{create:true});const f=await pos.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});const w=await f.createWritable();await w.write('fixture');await w.close();window.showDirectoryPicker=async()=>root;await d.chooseNgFolder();return d.analysisInputSnapshot();
 });expect(result.images).toHaveLength(1);expect(result.summaries.find(p=>p.position==='TCA').actualNg).toBe(1);
 await page.evaluate(async()=>{const d=window.__VISIONQC_DEBUG__,root=await (await navigator.storage.getDirectory()).getDirectoryHandle('alias-root');const pos=await root.getDirectoryHandle('CA(TOP)');const nested=await pos.getDirectoryHandle('AN(TOP)',{create:true});await nested.getFileHandle('20260203080000_P163GG22M2100001.png',{create:true});await d.chooseNgFolder();});
 await expect(page.locator('#vq43-toast')).toContainText('충돌');expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.analysisInputSnapshot())).images).toHaveLength(1);
});
