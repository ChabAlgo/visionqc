import { expect, test } from '@playwright/test';

async function open(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil:'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout:15000 });
}

test('Threshold control stays inside every narrow Tool card', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedReport());
  const bounds = await page.locator('.vq43-main-tools .vq43-tool-donut-item').evaluateAll((cards) => cards.map((card) => {
    const input = card.querySelector('.vq43-threshold-input');
    const cardRect = card.getBoundingClientRect();
    const inputRect = input.getBoundingClientRect();
    return { left:inputRect.left-cardRect.left, right:cardRect.right-inputRect.right, width:inputRect.width, height:inputRect.height, appearance:getComputedStyle(input).appearance };
  }));
  expect(bounds.length).toBeGreaterThan(0);
  for (const item of bounds) {
    expect(item.left).toBeGreaterThanOrEqual(0);
    expect(item.right).toBeGreaterThanOrEqual(0);
    expect(item.width).toBeGreaterThanOrEqual(47);
    expect(item.height).toBeGreaterThanOrEqual(29);
    expect(item.appearance).not.toBe('none');
  }
});

test('History mode limits Workspace choices to actual matching records', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedHistoryFilters());
  const mode = page.locator('[data-history-field="workspaceType"]');
  const workspace = page.locator('[data-history-field="workspaceKey"]');
  await mode.selectOption('green');
  await expect(workspace.locator('option')).toHaveCount(2);
  await expect(workspace.locator('option').nth(1)).toHaveText('green · Green A');
  await mode.selectOption('integrated');
  await expect(workspace.locator('option')).toHaveCount(3);
  await expect(workspace.locator('option').nth(1)).toContainText('integrated');
  await expect(workspace.locator('option').nth(2)).toContainText('integrated');
});

test('Main package buttons ask before triggering a download', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedDashboard());
  await page.evaluate(() => {
    window.__vqDownloadClicks = 0;
    HTMLAnchorElement.prototype.click = function(){ window.__vqDownloadClicks += 1; };
    window.confirm = () => false;
  });
  await page.locator('.vq43-top-actions [data-vq-action="simulation-agent-download"]').click();
  expect(await page.evaluate(() => window.__vqDownloadClicks)).toBe(0);
  await page.evaluate(() => { window.confirm = () => true; });
  await page.locator('.vq43-top-actions [data-vq-action="simulation-agent-download"]').click();
  expect(await page.evaluate(() => window.__vqDownloadClicks)).toBe(1);
});

test('Simulation status owns controls, Runtime tools are colored and option scroll chains outward', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:820 });
  await open(page);
  const toolClasses = await page.evaluate(() => window.__VISIONQC_DEBUG__.seedRuntimeToolColors());
  await expect(page.locator('.vq43-sim-status-actions #vq43-sim-start')).toBeVisible();
  await expect(page.locator('.vq43-sim-status-actions #vq43-sim-stop')).toBeVisible();
  expect(toolClasses.some((value) => value.includes('green'))).toBe(true);
  expect(toolClasses.some((value) => value.includes('blue'))).toBe(true);
  expect(toolClasses.some((value) => value.includes('red'))).toBe(true);
  const chained = await page.evaluate(async () => {
    const options = document.querySelector('.vq43-sim-options-scroll');
    const outer = document.querySelector('#vq43-page');
    options.scrollTop = options.scrollHeight;
    outer.scrollTop = 0;
    const before = outer.scrollTop;
    options.dispatchEvent(new WheelEvent('wheel', { bubbles:true, cancelable:true, deltaY:300 }));
    await new Promise((resolve) => requestAnimationFrame(resolve));
    return { before, after:outer.scrollTop, optionsScrollable:options.scrollHeight > options.clientHeight };
  });
  expect(chained.optionsScrollable).toBe(true);
  expect(chained.after).toBeGreaterThan(chained.before);
});
