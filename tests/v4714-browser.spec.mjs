import { expect, test } from '@playwright/test';

async function open(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil:'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout:15000 });
}

test('Threshold spinner is visible and analysis note follows the filters', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedReport());
  const threshold = await page.locator('.vq43-main-tools .vq43-threshold-input').first().evaluate((node) => {
    const rect = node.getBoundingClientRect();
    return { width:rect.width, height:rect.height, appearance:getComputedStyle(node).appearance };
  });
  expect(threshold.width).toBeGreaterThanOrEqual(63);
  expect(threshold.height).toBeGreaterThanOrEqual(31);
  expect(threshold.appearance).not.toBe('none');

  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedAnalysis());
  const order = await page.evaluate(() => [...document.querySelector('.vq43-analysis-left').children].map((node) => node.className));
  expect(order).toEqual(['vq43-filter','vq43-note vq43-analysis-scope-note','vq43-kpi-grid']);
});

test('live analysis refresh waits until an active click completes', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedAnalysis());
  const opened = await page.evaluate(async () => {
    const button = document.querySelector('.vq43-dropdown[data-vq-dropdown="scope"] .vq43-dropdown-button');
    button.dispatchEvent(new PointerEvent('pointerdown', { bubbles:true, pointerId:7, button:0 }));
    window.__VISIONQC_DEBUG__.queueLiveUiRefresh();
    button.dispatchEvent(new PointerEvent('pointerup', { bubbles:true, pointerId:7, button:0 }));
    button.click();
    await new Promise((resolve) => setTimeout(resolve, 180));
    return {
      connected:button.isConnected,
      open:document.querySelector('.vq43-dropdown[data-vq-dropdown="scope"]')?.classList.contains('open')
    };
  });
  expect(opened.connected).toBe(true);
  expect(opened.open).toBe(true);
});

test('score viewer shows one merged source and follows graph order with heatmap and zoom preserved', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  const seeded = await page.evaluate(() => window.__VISIONQC_DEBUG__.openScoreViewerRegression());
  expect(seeded.points).toBe(3);
  expect(seeded.images).toBe(1);
  await expect(page.locator('#vq43-modal')).toHaveClass(/open/);
  await expect(page.locator('.vq43-modal-head strong')).toContainText('P163GG23M2100001');
  await expect(page.locator('.vq43-modal-delete')).toBeVisible();
  await expect(page.locator('.vq43-modal-path b')).toHaveText('Score 1 / 3');

  const fit = await page.locator('#vq43-modal-zoom-image').evaluate((image) => {
    const imageRect = image.getBoundingClientRect();
    const layerRect = image.parentElement.getBoundingClientRect();
    const style = getComputedStyle(image);
    return {
      objectFit:style.objectFit,
      sameWidth:Math.abs(imageRect.width - layerRect.width) <= 1,
      sameHeight:Math.abs(imageRect.height - layerRect.height) <= 1
    };
  });
  expect(fit).toEqual({ objectFit:'contain', sameWidth:true, sameHeight:true });

  await page.getByRole('button', { name:'Crack Heatmap' }).click();
  await page.locator('#vq43-modal-viewport').dispatchEvent('wheel', { deltaY:-120 });
  const zoomBefore = await page.locator('#vq43-modal-zoom-value').textContent();
  expect(parseInt(zoomBefore, 10)).toBeGreaterThan(100);
  await page.keyboard.press('ArrowRight');
  await expect(page.locator('.vq43-modal-head strong')).toContainText('P163GG23M2100002');
  await expect(page.getByRole('button', { name:'Crack Heatmap' })).toHaveClass(/active/);
  await expect(page.locator('#vq43-modal-zoom-value')).toHaveText(zoomBefore);
  await expect(page.locator('.vq43-modal-path b')).toHaveText('Score 2 / 3');
});
