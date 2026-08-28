import { expect, test } from '@playwright/test';

async function open(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil:'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout:15000 });
}

const svgFile = (index) => ({
  name:`classification-${String(index).padStart(2, '0')}.svg`,
  mimeType:'image/svg+xml',
  buffer:Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${2400 + index * 80}" height="1600"><rect width="100%" height="100%" fill="#${index % 2 ? '345678' : '765432'}"/><text x="2100" y="800" fill="white" font-size="90">${index}</text></svg>`)
});

async function normalizedFocus(page) {
  return page.locator('main .cursor-grab').evaluate((viewport) => ({
    x:(viewport.scrollLeft + viewport.clientWidth / 2) / Math.max(1, viewport.scrollWidth),
    y:(viewport.scrollTop + viewport.clientHeight / 2) / Math.max(1, viewport.scrollHeight),
    left:viewport.scrollLeft,
    width:viewport.scrollWidth
  }));
}

test('classification preserves a right-side focus above 100 percent during normal and rapid navigation', async ({ page }) => {
  await open(page);
  const files = Array.from({ length:10 }, (_, index) => svgFile(index + 1));
  await page.locator('input[type="file"][accept="image/*"]').setInputFiles(files);
  await expect(page.locator('main img[alt="classification-01.svg"]').first()).toBeVisible();
  await page.getByTitle('Toggle Fit/Zoom').click();
  await page.getByTitle('Zoom In').click();
  await page.getByTitle('Zoom In').click();
  await expect(page.getByText('120%', { exact:true })).toBeVisible();
  const viewport = page.locator('main .cursor-grab');
  await viewport.evaluate((element) => { element.scrollLeft = element.scrollWidth - element.clientWidth; element.scrollTop = element.scrollHeight * .35; });
  const before = await normalizedFocus(page);
  expect(before.x).toBeGreaterThan(.75);

  await page.keyboard.press('ArrowRight');
  await expect(page.locator('main img[alt="classification-02.svg"]').first()).toBeVisible();
  await page.waitForTimeout(100);
  const afterOne = await normalizedFocus(page);
  expect(Math.abs(afterOne.x - before.x)).toBeLessThan(.04);
  expect(Math.abs(afterOne.y - before.y)).toBeLessThan(.04);

  for (let index = 0; index < 8; index += 1) {
    await page.keyboard.press('ArrowRight');
    await page.waitForTimeout(12);
  }
  await expect(page.locator('main img[alt="classification-10.svg"]').first()).toBeVisible();
  await page.waitForTimeout(150);
  const afterRapid = await normalizedFocus(page);
  expect(afterRapid.x).toBeGreaterThan(.72);
  expect(Math.abs(afterRapid.x - before.x)).toBeLessThan(.06);
  expect(Math.abs(afterRapid.y - before.y)).toBeLessThan(.06);
});

test('dashboard chart, Position cards, misses and Threshold inputs fit the requested geometry', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedReport());
  const result = await page.evaluate(() => {
    const svg = document.querySelector('.vq43-main-history-dashboard .vq43-history-line');
    const polyline = svg?.querySelector('polyline');
    const positionGrid = document.querySelector('.vq43-main-position .vq43-position-grid');
    const threshold = document.querySelector('.vq43-main-tools .vq43-threshold-input');
    const labels = [...document.querySelectorAll('.vq43-main-position .vq43-mini-grid span')].slice(0,4).map((node) => node.textContent);
    const svgRect = svg?.getBoundingClientRect();
    const polyRect = polyline?.getBoundingClientRect();
    return {
      chartFill:svgRect && polyRect ? polyRect.width / svgRect.width : 0,
      positionColumns:getComputedStyle(positionGrid).gridTemplateColumns.split(' ').length,
      labels,
      missesNested:document.querySelector('.vq43-main-misses')?.parentElement?.classList.contains('vq43-main-column-left'),
      thresholdWidth:threshold?.getBoundingClientRect().width || 0,
      thresholdFont:parseFloat(getComputedStyle(threshold).fontSize)
    };
  });
  expect(result.chartFill).toBeGreaterThan(.9);
  expect(result.positionColumns).toBe(2);
  expect(result.labels).toEqual(['실제 NG','CSV 매칭','정상 검출','미검']);
  expect(result.missesNested).toBe(true);
  expect(result.thresholdWidth).toBeGreaterThanOrEqual(63);
  expect(result.thresholdFont).toBeLessThanOrEqual(10);
});

test('analysis uses the requested left-right order and readable compact light text', async ({ page }) => {
  await page.setViewportSize({ width:1920, height:1080 });
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedAnalysis());
  const dark = await page.evaluate(() => {
    const box = (selector) => document.querySelector(selector).getBoundingClientRect();
    const left = box('.vq43-analysis-left'), right = box('.vq43-analysis-right');
    return {
      leftBeforeRight:left.left < right.left,
      filterInLeft:document.querySelector('.vq43-analysis-left>.vq43-filter') !== null,
      kpiInLeft:document.querySelector('.vq43-analysis-left>.vq43-kpi-grid') !== null,
      rightOrder:[...document.querySelector('.vq43-analysis-right').children].map((node) => node.className),
      titleSize:parseFloat(getComputedStyle(document.querySelector('.vq43-analysis-page .vq43-title')).fontSize),
      noOverflow:document.querySelector('.vq43-analysis-upper-grid').scrollWidth <= document.querySelector('.vq43-analysis-upper-grid').clientWidth + 1
    };
  });
  expect(dark.leftBeforeRight).toBe(true);
  expect(dark.filterInLeft).toBe(true);
  expect(dark.kpiInLeft).toBe(true);
  expect(dark.rightOrder).toEqual(['vq43-analysis-export','vq43-actual-ng-minimum','vq43-table vq43-summary-table']);
  expect(dark.titleSize).toBeLessThanOrEqual(25);
  expect(dark.noOverflow).toBe(true);

  await page.locator('[data-vq-action="theme-toggle"]').click();
  const lightColor = await page.locator('.vq43-analysis-page .vq43-subtitle').evaluate((node) => getComputedStyle(node).color);
  expect(lightColor).toBe('rgb(54, 81, 108)');
});

test('running Simulation progress remains fixed while moving between menus', async ({ page }) => {
  await open(page);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.seedRunningProgress());
  const progress = page.locator('#vq43-global-sim-progress');
  await expect(progress).toBeVisible();
  await expect(page.locator('#vq43-global-sim-percent')).toHaveText('37.00%');
  await page.locator('[data-vq-page="analysis"]').click();
  await expect(progress).toBeVisible();
  const fixed = await progress.evaluate((node) => ({ position:getComputedStyle(node).position, bottom:getComputedStyle(node).bottom }));
  expect(fixed.position).toBe('fixed');
  expect(parseFloat(fixed.bottom)).toBeGreaterThanOrEqual(0);
  await progress.getByRole('button', { name:'시뮬레이션 보기' }).click();
  await expect(page.locator('.vq43-nav-item[data-vq-page="simulation"]')).toHaveClass(/active/);
  await page.evaluate(() => window.__VISIONQC_DEBUG__.stopRunningProgress());
  await expect(progress).toBeHidden();
});
