import { test, expect } from '@playwright/test';
for(const theme of ['dark','light']) test('independent Green switch preserves legacy options: '+theme,async({page},testInfo)=>{
  const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(theme=>{
    localStorage.setItem('visionqc-v43-active-page','simulation');
    localStorage.setItem('visionqc-v472-theme',theme);
  },theme);
  await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
  await expect(page.locator('body')).toHaveClass(theme === 'light' ? /vq43-theme-light/ : /^(?!.*vq43-theme-light).*$/);
  await page.locator('[data-vq-action="simulation-mode"][data-vq-mode="green"]').click();
  const check=page.locator('[data-sim-scope="green"][data-sim-field="originalProcess"]');
  await expect(check).toBeChecked();
  await expect(page.locator('[data-sim-field="disableTensorRt"]')).toHaveCount(0);
  await check.uncheck();
  await expect(page.locator('[data-sim-field="disableTensorRt"]')).toBeVisible();
  await page.locator('[data-sim-field="disableTensorRt"]').check();
  await check.check();
  await check.uncheck();
  await expect(page.locator('[data-sim-field="disableTensorRt"]')).toBeChecked();
  await check.check();
  await page.locator('[data-vq-action="simulation-mode"][data-vq-mode="integrated"]').click();
  await expect(check).toHaveCount(0);
  await page.locator('[data-vq-action="simulation-mode"][data-vq-mode="green"]').click();
  await expect(check).toBeChecked();
  await page.locator('.vq43-sim-options').screenshot({path:testInfo.outputPath('green-options-'+theme+'.png')});
  expect(errors).toEqual([]);
});
