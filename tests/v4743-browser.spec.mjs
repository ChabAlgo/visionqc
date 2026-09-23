import {test,expect} from '@playwright/test';
import {readFile} from 'node:fs/promises';
async function setup(page){
 await page.route('http://127.0.0.1:*/api/**',r=>r.fulfill({contentType:'application/json',body:'{"ok":false}'}));
 await page.goto('/index.html?vqDebug=1&browserRegression=1');
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 await page.locator('input[type="file"][accept="image/*"]').setInputFiles(['a','b'].map(n=>({name:n+'.svg',mimeType:'image/svg+xml',buffer:Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50"/>')})));
 await expect(page.locator('main img[alt="a.svg"]').first()).toBeVisible();
}
async function labels(page){return page.evaluate(()=>new Promise(resolve=>{
 window.addEventListener('visionqc:label-cells-data',e=>resolve(e.detail.items.map(i=>i.labels)),{once:true});
 window.dispatchEvent(new CustomEvent('visionqc:export-label-cells'));
}));}
test('clear shortcut is configurable, rejects collisions and persists',async({page})=>{
 await setup(page);
 await page.getByRole('button',{name:'Class Settings',exact:true}).click();
 await page.getByLabel('현재 이미지 라벨 해제 키').selectOption('O');
 await page.getByRole('button',{name:'Save',exact:true}).last().click();
 await expect(page.getByText('라벨 해제 키가 다른 단축키와 중복됩니다. 다른 키를 선택해 주세요.',{exact:true})).toBeVisible();
 await page.getByLabel('현재 이미지 라벨 해제 키').selectOption('X');
 await page.getByRole('button',{name:'Save',exact:true}).last().click();
 await page.keyboard.press('o');await page.keyboard.press('ArrowLeft');
 expect(await labels(page)).toEqual([['OK'],[]]);
 await page.keyboard.press('Backspace');expect(await labels(page)).toEqual([['OK'],[]]);
 await page.keyboard.press('Control+x');expect(await labels(page)).toEqual([['OK'],[]]);
 await page.keyboard.press('x');expect(await labels(page)).toEqual([[],[]]);
 await expect(page.locator('main img[alt="a.svg"]').first()).toBeVisible();
 await setup(page);await page.getByRole('button',{name:'Class Settings',exact:true}).click();
 await expect(page.getByLabel('현재 이미지 라벨 해제 키')).toHaveValue('X');
});
test('default Backspace clears all labels only on current image',async({page})=>{
 await setup(page);await page.keyboard.press('o');await page.keyboard.press('o');await page.keyboard.press('ArrowLeft');
 await page.keyboard.down('m');await page.keyboard.press('1');await page.keyboard.press('2');await page.keyboard.up('m');
 expect((await labels(page))[0]).toEqual(['OK','CRACK','ETC']);
 await page.keyboard.press('Backspace');expect(await labels(page)).toEqual([[],['OK']]);
 await expect(page.locator('main img[alt="a.svg"]').first()).toBeVisible();
});

test('class preset JSON backs up slots and draft, restores after browser storage loss, and rejects invalid input atomically',async({page})=>{
 await setup(page);
 const dialogs=[];
 page.on('dialog',async d=>{dialogs.push(d.message());await d.accept();});
 await page.getByRole('button',{name:'Class Settings',exact:true}).click();
 const modal=page.locator('.vq-class-settings');
 const names=modal.locator('.vq-class-row input').filter({visible:true});
 await names.first().fill('정상 백업');
 await page.getByLabel('현재 이미지 라벨 해제 키').selectOption('X');
 await modal.getByRole('button',{name:'Save',exact:true}).first().click();
 const before=await page.evaluate(()=>JSON.parse(localStorage.getItem('visionqc-class-settings-profiles-v1')));
 const pending=page.waitForEvent('download');
 await modal.getByRole('button',{name:'JSON 내보내기',exact:true}).click();
 const download=await pending;
 const backup=await readFile(await download.path());
 const data=JSON.parse(backup.toString());
 expect(data.presets).toEqual(before);
 expect(data.current.classConfigs[0].label).toBe('정상 백업');
 expect(data.current.shortcutSettings.clearLabelKey).toBe('X');
 await page.evaluate(()=>localStorage.removeItem('visionqc-class-settings-profiles-v1'));
 await modal.getByRole('button',{name:'Close',exact:true}).click();
 await page.getByRole('button',{name:'Class Settings',exact:true}).click();
 const picker=page.getByLabel('Class Presets JSON 가져오기');
 await picker.setInputFiles({name:'backup.json',mimeType:'application/json',buffer:backup});
 await expect(names.first()).toHaveValue('정상 백업');
 await expect.poll(()=>page.evaluate(()=>JSON.parse(localStorage.getItem('visionqc-class-settings-profiles-v1')))).toEqual(before);
 const invalid=structuredClone(data);invalid.presets.class1.classConfigs[1].hotkey=invalid.presets.class1.classConfigs[0].hotkey;
 await picker.setInputFiles({name:'invalid.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(invalid))});
 await expect.poll(()=>dialogs.at(-1)).toContain('JSON 가져오기 실패');
 expect(await page.evaluate(()=>JSON.parse(localStorage.getItem('visionqc-class-settings-profiles-v1')))).toEqual(before);
 await expect(names.first()).toHaveValue('정상 백업');
 // Save collision must raise a dialog even when the inline message is below the viewport.
 await modal.locator('.vq-class-row').nth(1).locator('input').nth(1).fill('O');
 await modal.getByRole('button',{name:'Save',exact:true}).last().click();
 await expect.poll(()=>dialogs.at(-1)).toContain('단축키가 중복되었습니다');
 await expect(modal).toBeVisible();
});

test('class preset cancellation, malformed JSON and storage failure preserve existing presets',async({page})=>{
 await setup(page);
 let acceptImport=true;const messages=[];
 page.on('dialog',async d=>{messages.push(d.message());if(d.type()==='confirm'&&!acceptImport)await d.dismiss();else await d.accept();});
 await page.getByRole('button',{name:'Class Settings',exact:true}).click();
 const modal=page.locator('.vq-class-settings'),picker=page.getByLabel('Class Presets JSON 가져오기');
 await modal.getByRole('button',{name:'Save',exact:true}).first().click();
 const original=await page.evaluate(()=>localStorage.getItem('visionqc-class-settings-profiles-v1'));
 const waitDownload=page.waitForEvent('download');await modal.getByRole('button',{name:'JSON 내보내기',exact:true}).click();
 const backup=JSON.parse(await readFile(await (await waitDownload).path(),'utf8'));
 backup.current.classConfigs[0].label='가져온 이름';backup.presets={};
 const input={name:'backup.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(backup))};
 acceptImport=false;await picker.setInputFiles(input);
 await expect.poll(()=>messages.at(-1)).toContain('교체할까요');
 expect(await page.evaluate(()=>localStorage.getItem('visionqc-class-settings-profiles-v1'))).toBe(original);
 await expect(modal.locator('.vq-class-row input').first()).toHaveValue('OK');
 await picker.setInputFiles({name:'broken.json',mimeType:'application/json',buffer:Buffer.from('{broken')});
 await expect.poll(()=>messages.at(-1)).toContain('JSON 가져오기 실패');
 acceptImport=true;
 await page.evaluate(()=>{const original=Storage.prototype.setItem;Storage.prototype.setItem=function(key,value){if(key==='visionqc-class-settings-profiles-v1')throw new DOMException('Quota exceeded','QuotaExceededError');return original.call(this,key,value);};});
 await picker.setInputFiles(input);
 await expect.poll(()=>messages.at(-1)).toContain('JSON 가져오기 실패');
 expect(await page.evaluate(()=>localStorage.getItem('visionqc-class-settings-profiles-v1'))).toBe(original);
 await expect(modal.locator('.vq-class-row input').first()).toHaveValue('OK');
 await modal.getByRole('button',{name:'Save',exact:true}).first().click();
 await expect.poll(()=>messages.at(-1)).toContain('JSON 내보내기로 현재 설정을 백업');
 // A failed browser write must not prevent an independent file backup.
 const downloadable=page.waitForEvent('download');await modal.getByRole('button',{name:'JSON 내보내기',exact:true}).click();
 expect((await downloadable).suggestedFilename()).toMatch(/VisionQC_ClassPresets_.*\.json/);
});
