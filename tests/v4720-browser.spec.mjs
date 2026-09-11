import { test, expect } from '@playwright/test';
test('filename settings use one timestamp field and retain old profile rules', async ({ page }, testInfo) => {
  await page.addInitScript(() => localStorage.setItem('visionqc-v43-active-page','settings'));
  await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
  const card=page.locator('.vq43-naming-card');
  await expect(card).toBeVisible();
  await expect(card.locator(':scope > .vq43-naming-rule-grid > fieldset')).toHaveCount(2);
  await expect(card.locator('[data-naming-field="dateTime"][data-naming-key="mode"]')).toHaveValue('compact');
  await expect(card.locator('.vq43-naming-example')).toContainText('2026-08-07 07:47:05');
  await card.locator('[data-naming-field="dateTime"][data-naming-key="mode"]').selectOption('token');
  await card.locator('[data-naming-field="dateTime"][data-naming-key="tokenIndex"]').fill('3');
  await card.locator('[data-vq-action="naming-profile-save"]').click();
  await page.reload();
  await expect(card.locator('[data-naming-field="dateTime"][data-naming-key="mode"]')).toHaveValue('token');
  await expect(card.locator('[data-naming-field="dateTime"][data-naming-key="tokenIndex"]')).toHaveValue('3');
  const profile=await page.evaluate(()=>JSON.parse(localStorage.getItem('visionqc-v450-naming-profile')));
  expect(profile.cellId.extractLength).toBe(16);
  expect(profile.date.mode).toBe('auto'); expect(profile.time.mode).toBe('auto');
  await card.screenshot({path:testInfo.outputPath('filename-settings.png')});
});
test('legacy non-adjacent timestamp positions survive opening and saving settings', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('visionqc-v43-active-page','settings');
    localStorage.setItem('visionqc-v450-naming-profile',JSON.stringify({
      cellId:{mode:'auto',candidateLength:18,extractLength:16,requireLetter:true},
      date:{mode:'token',tokenIndex:1},time:{mode:'token',tokenIndex:6}
    }));
  });
  await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
  await expect(page.locator('[data-naming-field="dateTime"][data-naming-key="mode"]')).toHaveValue('legacy');
  await page.locator('[data-vq-action="naming-profile-save"]').click();
  const profile=await page.evaluate(()=>JSON.parse(localStorage.getItem('visionqc-v450-naming-profile')));
  expect(profile.dateTime.mode).toBe('legacy');expect(profile.time.tokenIndex).toBe(6);
});

test('filename rules export and validated import without changing the saved profile list', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('visionqc-v43-active-page','settings'));
  await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
  const card=page.locator('.vq43-naming-card');
  await card.locator('#vq43-naming-name').fill('라인 A 규칙');
  await card.locator('#vq43-naming-delimiter').fill('-');
  await card.locator('[data-naming-field="cellId"][data-naming-key="candidateLength"]').fill('20');
  await card.locator('[data-naming-field="cellId"][data-naming-key="extractLength"]').fill('16');
  await card.locator('[data-naming-field="dateTime"][data-naming-key="mode"]').selectOption('split');
  const downloadPromise=page.waitForEvent('download');
  await card.locator('[data-vq-action="naming-profile-export"]').click();
  const download=await downloadPromise;
  expect(download.suggestedFilename()).toBe('VisionQC_라인 A 규칙_v1.json');
  const exported=JSON.parse(await (await download.createReadStream()).toArray().then(parts=>Buffer.concat(parts).toString('utf8')));
  expect(exported.format).toBe('visionqc-naming-profile');
  expect(exported.schemaVersion).toBe(1);
  expect(exported.profile.name).toBe('라인 A 규칙');
  expect(exported.profile.delimiter).toBe('-');
  expect(exported.profile.cellId.candidateLength).toBe(20);
  expect(exported.profile.dateTime.mode).toBe('split');

  await card.locator('#vq43-naming-import').setInputFiles({name:'rule.json',mimeType:'application/json',buffer:Buffer.from('\uFEFF'+JSON.stringify({...exported,profile:{...exported.profile,name:'가져온 규칙',version:7,delimiter:'__'}}))});
  await expect(card.locator('#vq43-naming-name')).toHaveValue('가져온 규칙');
  await expect(card.locator('#vq43-naming-delimiter')).toHaveValue('__');
  const state=await page.evaluate(()=>({active:JSON.parse(localStorage.getItem('visionqc-v450-naming-profile')),saved:localStorage.getItem('visionqc-v4725-naming-profiles')}));
  expect(state.active.name).toBe('가져온 규칙');
  expect(state.active.version).toBe(7);
  expect(state.saved).toBeNull();

  await card.locator('#vq43-naming-import').setInputFiles({name:'bad.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify({...exported,schemaVersion:99}))});
  await expect(page.locator('#vq43-toast')).toContainText('지원하지 않는 규칙 파일 버전');
  expect((await page.evaluate(()=>JSON.parse(localStorage.getItem('visionqc-v450-naming-profile')))).name).toBe('가져온 규칙');
});
