import { expect, test } from '@playwright/test';

async function open(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil:'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout:15000 });
}

test('Viewer 100 percent contains the full image and Heatmap supports keyboard navigation', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.openViewerRegression());
  const geometry = await page.evaluate(() => {
    const viewport = document.querySelector('#vq43-modal-viewport').getBoundingClientRect();
    const image = document.querySelector('#vq43-modal-zoom-image').getBoundingClientRect();
    return { viewport, image, zoom:document.querySelector('#vq43-modal-zoom-value').textContent };
  });
  expect(geometry.zoom).toBe('100%');
  expect(geometry.image.left).toBeGreaterThanOrEqual(geometry.viewport.left - 1);
  expect(geometry.image.right).toBeLessThanOrEqual(geometry.viewport.right + 1);
  expect(geometry.image.top).toBeGreaterThanOrEqual(geometry.viewport.top - 1);
  expect(geometry.image.bottom).toBeLessThanOrEqual(geometry.viewport.bottom + 1);

  await page.getByRole('button', { name:'Crack Heatmap' }).click();
  await expect(page.locator('.vq43-modal-nav')).toHaveCount(2);
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('button', { name:'Welding Heatmap' })).toHaveClass(/active/);
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByRole('button', { name:'Crack Heatmap' })).toHaveClass(/active/);
});

test('Main and Analysis use the requested compact two-column geometry', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedReport());
  const main = await page.evaluate(() => {
    const dashboard = document.querySelector('.vq43-main-dashboard');
    const columns = document.querySelectorAll('.vq43-main-column');
    const toolGrid = document.querySelector('.vq43-main-tools .vq43-tool-position-grid');
    const cell = document.querySelector('.vq43-main-cell').getBoundingClientRect();
    const position = document.querySelector('.vq43-main-position').getBoundingClientRect();
    return {
      rowGap:getComputedStyle(dashboard).rowGap,
      independentColumns:columns.length,
      toolColumns:getComputedStyle(toolGrid).gridTemplateColumns.split(' ').length,
      verticalGap:Math.round(position.top - cell.bottom),
      labels:[...document.querySelectorAll('.vq43-main-position .vq43-mini-grid span')].slice(0,4).map(x => x.innerText)
    };
  });
  expect(main.rowGap).toBe('0px');
  expect(main.independentColumns).toBe(2);
  expect(main.toolColumns).toBe(2);
  expect(main.verticalGap).toBeLessThanOrEqual(1);
  expect(main.labels).toEqual(['실제 NG','CSV 매칭','정상 검출','미검']);

  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedAnalysis());
  const analysis = await page.evaluate(() => {
    const grid = document.querySelector('.vq43-analysis-upper-grid');
    return { columns:getComputedStyle(grid).gridTemplateColumns.split(' ').length, overflow:grid.scrollWidth-grid.clientWidth };
  });
  expect(analysis.columns).toBe(2);
  expect(analysis.overflow).toBeLessThanOrEqual(1);
});
