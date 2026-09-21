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
    const circles = [...svg.querySelectorAll('.vq43-history-point circle')].map(node => parseFloat(node.getAttribute('cx')));
    const viewWidth = 100;
    return { first:circles[0], last:circles.at(-1), viewWidth };
  });
  expect(geometry.first).toBeGreaterThan(5);
  expect(geometry.last).toBeLessThan(geometry.viewWidth - 1);
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

test('loading a saved result clears a stale dashboard date and shows the new rows', async ({ page }) => {
  // This verifies the browser fallback; a real installed Agent must not switch it to the native picker.
  await page.route('http://127.0.0.1:*/api/**', route => route.abort());
  await page.addInitScript(() => {
    try { delete window.showOpenFilePicker; } catch {}
    localStorage.clear();
  });
  await open(page);
  await page.evaluate(() => {
    const row = (cellId, captureTimestamp) => ({
      cellId, position:'AN(TOP)', captureTimestamp, totalResult:'NG',
      tools:{ Crack:{ tool:'Crack', result:'NG', score:0.9 } }
    });
    window.__VISIONQC_DEBUG__.seedRows([
      row('OLD000000000001', '2026-08-24T08:00:00'),
      row('OLD000000000002', '2026-09-03T09:00:00')
    ]);
  });
  await page.locator('[data-vq-action="dashboard-day"]').first().click();
  await page.locator('[data-vq-page="settings"]').click();

  const chooser = page.waitForEvent('filechooser');
  await page.locator('[data-vq-position="AN(TOP)"] [data-vq-action="choose-result"]').click();
  await (await chooser).setFiles({
    name:'saved-result.csv', mimeType:'text/csv',
    buffer:Buffer.from('Cell ID,Position,Total_result,CaptureTimestamp,Crack_Result,Crack_Score\nP163GG23M2100001,AN(TOP),OK,2026-10-05T07:00:00,OK,0.8\n')
  });
  await expect(page.locator('.vq43-input-row[data-vq-position="AN(TOP)"]')).toContainText('saved-result.csv');
  await page.locator('[data-vq-page="main"]').click();

  const snapshot = await page.evaluate(() => window.__VISIONQC_DEBUG__.dateSnapshot());
  expect(snapshot.selected).toBe('');
  expect(snapshot.total).toBe(1);
  expect(snapshot.keys).toEqual(['AN(TOP)|P163GG23M2100001|2026-10-05']);
});
