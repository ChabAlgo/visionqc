import {test,expect} from '@playwright/test';
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
