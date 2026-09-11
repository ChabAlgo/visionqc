import { expect, test } from '@playwright/test';

async function open(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil:'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout:15000 });
}

test('daily NG points keep margins at both ends of the plot', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  await page.evaluate(() => {
    const rows = [
      { cellId:'MARGIN0000000001', position:'AN(TOP)', captureTimestamp:'2026-09-09T08:00:00', totalResult:'NG', tools:{} },
      { cellId:'MARGIN0000000002', position:'AN(TOP)', captureTimestamp:'2026-09-10T08:00:00', totalResult:'OK', tools:{} }
    ];
    window.__VISIONQC_DEBUG__.seedRows(rows);
  });
  const geometry = await page.locator('.vq43-main-history-dashboard .vq43-history-line').evaluate((svg) => {
    const circles = [...svg.querySelectorAll('.vq43-history-point circle')].map(node => Number(node.getAttribute('cx')));
    const viewWidth = Number(svg.viewBox.baseVal.width);
    return { first:circles[0], last:circles.at(-1), viewWidth };
  });
  expect(geometry.first).toBeGreaterThan(28);
  expect(geometry.last).toBeLessThan(geometry.viewWidth - 6);
});

test('Runtime-discovered Edge is added once and sent to the Agent', async ({ page }) => {
  await open(page);
  const result = await page.evaluate(() => window.__VISIONQC_DEBUG__.seedRuntimeToolSync());
  expect(result.added).toEqual(['Edge']);
  expect(result.configured).toEqual(['Separator', 'Edge']);
  expect(result.requested).toEqual(['Separator', 'Edge']);
});

test('Tool card metadata wraps without overlap', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedReport());
  const result = await page.locator('.vq43-main-tools .vq43-tool-donut-item').first().evaluate((card) => {
    const score = card.querySelector('.vq43-tool-score-meta>span').getBoundingClientRect();
    const threshold = card.querySelector('.vq43-tool-score-meta label').getBoundingClientRect();
    const input = card.querySelector('.vq43-threshold-input').getBoundingClientRect();
    const bounds = card.getBoundingClientRect();
    return { separated:score.bottom <= threshold.top + 1, inputInside:input.left >= bounds.left && input.right <= bounds.right, inputWidth:input.width };
  });
  expect(result.separated).toBe(true);
  expect(result.inputInside).toBe(true);
  expect(result.inputWidth).toBeGreaterThanOrEqual(47);
});
