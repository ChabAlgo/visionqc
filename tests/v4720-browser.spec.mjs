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
