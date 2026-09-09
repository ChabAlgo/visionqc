import { test, expect } from '@playwright/test';
for(const theme of ['dark','light']) test('production Green options preserve GPU and remove experiments: '+theme,async({page},testInfo)=>{
  const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(theme=>{
    localStorage.setItem('visionqc-v43-active-page','simulation');
    localStorage.setItem('visionqc-v472-theme',theme);
  },theme);
  await page.goto('/index.html?vqDebug=1&browserRegression=1',{waitUntil:'domcontentloaded'});
  await expect(page.locator('body')).toHaveClass(theme === 'light' ? /vq43-theme-light/ : /^(?!.*vq43-theme-light).*$/);
  await page.locator('[data-vq-action="simulation-mode"][data-vq-mode="green"]').click();
  for (const name of ['originalProcess','disableTensorRt','freshRuntime','detailedDiagnostics','disableOptimizedGpuMemory']) {
    await expect(page.locator('[data-sim-scope="green"][data-sim-field="'+name+'"]')).toHaveCount(0);
  }
  await expect(page.locator('[data-sim-scope="green"][data-sim-field="useGpu"]')).toBeVisible();
  await page.locator('[data-vq-action="simulation-mode"][data-vq-mode="integrated"]').click();
  await page.locator('[data-vq-action="simulation-mode"][data-vq-mode="green"]').click();
  await expect(page.locator('[data-sim-field="disableTensorRt"]')).toHaveCount(0);
  await page.locator('.vq43-sim-options').screenshot({path:testInfo.outputPath('green-options-'+theme+'.png')});
  expect(errors).toEqual([]);
});
