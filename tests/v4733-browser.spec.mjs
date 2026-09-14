import { expect, test } from '@playwright/test';

async function open(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil:'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout:15000 });
}

test('Position parallel options default on and max concurrency updates without losing the request', async ({ page }) => {
  await open(page);
  await page.evaluate(() => { window.__VISIONQC_DEBUG__.openSimulation(); });
  const initial = await page.evaluate(() => window.__VISIONQC_DEBUG__.simulationRequestSnapshot());
  expect(initial).toMatchObject({ parallelPositions:true, autoDistributeGpu:true, maxParallelPositions:10 });
  await expect(page.locator('[data-sim-scope="execution"][data-sim-field="parallelPositions"]')).toBeChecked();
  await expect(page.locator('[data-sim-scope="execution"][data-sim-field="autoDistributeGpu"]')).toBeChecked();
  const max = page.locator('[data-sim-scope="execution"][data-sim-field="maxParallelPositions"]');
  await max.fill('4');
  await max.dispatchEvent('change');
  const updated = await page.evaluate(() => window.__VISIONQC_DEBUG__.simulationRequestSnapshot());
  expect(updated.maxParallelPositions).toBe(4);
});

test('completed-run reconciliation replaces a partial live dashboard with every stored row', async ({ page }) => {
  await open(page);
  const result = await page.evaluate(() => window.__VISIONQC_DEBUG__.reconcileLiveRowsRegression());
  expect(result).toEqual({ partial:2, accepted:12, dashboardTotal:12, positionCounts:[3,3,3,3], syncedRunId:'debug-run' });
});

test('running DB synchronization adds only new rows and ignores duplicate SSE detail', async ({ page }) => {
  await open(page);
  const result = await page.evaluate(() => window.__VISIONQC_DEBUG__.liveDbIncrementalRegression());
  expect(result).toEqual({ afterIgnoredSse:0, liveRows:4, dashboardTotal:4, positionCounts:[2,2] });
});

test('live aggregation matches a complete rebuild across batches and duplicate cells', async ({ page }) => {
  await open(page);
  const result = await page.evaluate(() => window.__VISIONQC_DEBUG__.incrementalAggregationRegression());
  expect(result.appendedRows).toBe(4);
  expect(result.incremental).toEqual(result.complete);
  expect(result.equal).toBe(true);
});

for (const scenario of [
  { name:'Dashboard 미검 Cell ID', open:'openMissSequenceRegression', kind:'미검', cells:['MISS-CELL-001','MISS-CELL-002','MISS-CELL-003'] },
  { name:'검사 이력 Cell 이미지 탐색', open:'openHistorySequenceRegression', kind:'검사 이력', cells:['HISTORY-CELL-001','HISTORY-CELL-002','HISTORY-CELL-003'] }
]) {
  test(`${scenario.name} moves in order with arrow keys and stops at the boundaries`, async ({ page }) => {
    await open(page);
    await page.evaluate((method) => window.__VISIONQC_DEBUG__[method](), scenario.open);
    const snapshot = () => page.evaluate(() => window.__VISIONQC_DEBUG__.modalSequenceSnapshot());

    await expect.poll(snapshot).toMatchObject({ cellId:scenario.cells[0], kind:scenario.kind, index:0, count:3, previousDisabled:true, nextDisabled:false });
    await page.keyboard.press('ArrowRight');
    await expect.poll(snapshot).toMatchObject({ cellId:scenario.cells[1], index:1, previousDisabled:false, nextDisabled:false });
    await page.keyboard.press('ArrowDown');
    await expect.poll(snapshot).toMatchObject({ cellId:scenario.cells[2], index:2, previousDisabled:false, nextDisabled:true });
    await page.keyboard.press('ArrowRight');
    await expect.poll(snapshot).toMatchObject({ cellId:scenario.cells[2], index:2 });
    await page.keyboard.press('ArrowLeft');
    await expect.poll(snapshot).toMatchObject({ cellId:scenario.cells[1], index:1 });
    await page.keyboard.press('ArrowUp');
    await expect.poll(snapshot).toMatchObject({ cellId:scenario.cells[0], index:0, previousDisabled:true });
    await page.keyboard.press('ArrowLeft');
    await expect.poll(snapshot).toMatchObject({ cellId:scenario.cells[0], index:0 });
  });
}
