import { expect, test } from '@playwright/test';

async function openSimulation(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout: 15000 });
  await page.evaluate(() => window.__VISIONQC_DEBUG__.openSimulation());
  await expect(page.locator('.vq43-sim-page')).toBeVisible();
  await expect(page.locator('.vq43-sim-options-scroll')).toBeVisible();
}

test.describe('VisionQC v4.4.38 FHD interaction regression', () => {
  test('Tool add/remove preserves all edited runtime parameters', async ({ page }) => {
    await openSimulation(page);
    const progress = page.locator('input[data-sim-scope="green"][data-sim-field="printEvery"]');
    const quality = page.locator('input[data-sim-scope="green"][data-sim-field="jpegQuality"]');
    await progress.fill('3');
    await quality.fill('93');

    const toolRows = page.locator('.vq43-sim-table.tools tbody tr');
    const before = await toolRows.count();
    await page.locator('[data-vq-action="simulation-tool-add"]').click();
    await expect(toolRows).toHaveCount(before + 1);
    await expect(progress).toHaveValue('3');
    await expect(quality).toHaveValue('93');

    const firstTool = toolRows.first();
    const firstName = await firstTool.locator('[data-sim-tool-field="toolName"]').inputValue();
    await firstTool.locator('[data-sim-tool-field="selected"]').check();
    await page.locator('[data-vq-action="simulation-tool-remove"]').click();
    await expect(toolRows).toHaveCount(before);
    await expect(toolRows.first().locator('[data-sim-tool-field="toolName"]')).not.toHaveValue(firstName);
    await expect(progress).toHaveValue('3');
    await expect(quality).toHaveValue('93');
  });

  test('Keyword Input Root is disabled only while Keyword mode is off', async ({ page }) => {
    await openSimulation(page);
    const keywordMode = page.locator('input[data-sim-scope="integrated"][data-sim-field="keywordMode"]');
    const keywordRoot = page.locator('input[data-sim-scope="integrated"][data-sim-field="keywordInputRoot"]');
    const keywordBrowse = page.locator('button[data-sim-scope="integrated"][data-sim-field="keywordInputRoot"]');

    await keywordMode.uncheck();
    await expect(keywordRoot).toBeDisabled();
    await expect(keywordBrowse).toBeDisabled();
    await keywordMode.check();
    await expect(keywordRoot).toBeEnabled();
    await expect(keywordBrowse).toBeEnabled();
    await keywordMode.uncheck();
    await expect(keywordRoot).toBeDisabled();
    await expect(keywordBrowse).toBeDisabled();
  });

  test('disconnect cleanup removes loaded Workspace cards and preserves selected paths', async ({ page }) => {
    await openSimulation(page);
    const result = await page.evaluate(() => window.__VISIONQC_DEBUG__.testDisconnectWorkspaceClear());
    expect(result).toMatchObject({
      changed: true,
      greenInfoCleared: true,
      blueInfoCleared: true,
      pathsPreserved: true,
      loadedCards: 0
    });
  });

  test('debug regression covers scroll, selection, fallback and preview overflow', async ({ page }) => {
    await openSimulation(page);
    const result = await page.evaluate(() => window.__VISIONQC_DEBUG__.runSimulationUiRegression());

    expect(result, JSON.stringify(result, null, 2)).toMatchObject({
      scrollPreserved: true,
      simulationDomPreserved: true,
      selectionPreserved: true,
      optionsNoHorizontal: true,
      fallbackNoHorizontal: true,
      previewNoHorizontal: true,
      ok: true
    });
    expect(result.scrollBefore).toBeGreaterThan(0);
    expect(result.scrollAfter).toBe(result.scrollBefore);
  });

  test('real mouse release keeps text selected and a main click keeps Options scroll', async ({ page }) => {
    await openSimulation(page);

    const title = page.locator('.vq43-sim-options-head strong').first();
    const titleBox = await title.boundingBox();
    expect(titleBox).not.toBeNull();
    await page.mouse.move(titleBox.x + titleBox.width - 4, titleBox.y + titleBox.height / 2);
    await page.mouse.down();
    await page.mouse.move(titleBox.x + 4, titleBox.y + titleBox.height / 2, { steps: 12 });
    await page.mouse.up();

    const selectedOnRelease = await page.evaluate(() => window.getSelection()?.toString() || '');
    expect(selectedOnRelease.trim().length).toBeGreaterThan(3);
    await page.waitForTimeout(150);
    expect(await page.evaluate(() => window.getSelection()?.toString() || '')).toBe(selectedOnRelease);

    const options = page.locator('.vq43-sim-options-scroll');
    const before = await options.evaluate((element) => {
      element.scrollTop = Math.min(520, Math.max(0, element.scrollHeight - element.clientHeight));
      return element.scrollTop;
    });
    expect(before).toBeGreaterThan(0);

    const main = page.locator('.vq43-sim-maincol');
    const mainBox = await main.boundingBox();
    expect(mainBox).not.toBeNull();
    await page.mouse.click(mainBox.x + Math.min(12, mainBox.width / 2), mainBox.y + Math.min(12, mainBox.height / 2));
    await page.waitForTimeout(100);
    const after = await options.evaluate((element) => element.scrollTop);
    expect(after).toBe(before);
  });

  test('FHD panels and preview do not overflow horizontally', async ({ page }) => {
    await openSimulation(page);
    await page.evaluate(() => window.__VISIONQC_DEBUG__.openSimulationPreview());
    await expect(page.locator('.vq43-sim-preview-dialog')).toBeVisible();

    const geometry = await page.evaluate(() => {
      const selectors = [
        '.vq43-sim-options-scroll',
        '.vq43-sim-tools-wrap',
        '.vq43-fallback-list',
        '.vq43-sim-preview-dialog',
        '.vq43-sim-preview-grid'
      ];
      return Object.fromEntries(selectors.map((selector) => {
        const element = document.querySelector(selector);
        return [selector, element ? {
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
          overflow: element.scrollWidth - element.clientWidth
        } : null];
      }));
    });

    for (const [selector, dimensions] of Object.entries(geometry)) {
      expect(dimensions, `${selector} is missing`).not.toBeNull();
      expect(dimensions.overflow, `${selector}: ${JSON.stringify(dimensions)}`).toBeLessThanOrEqual(1);
    }
  });

  test('FHD Options does not cover upper content and fallback controls align', async ({ page }) => {
    await openSimulation(page);
    const result = await page.evaluate(() => {
      const rect = (selector) => {
        const element = document.querySelector(selector);
        if (!element) return null;
        const box = element.getBoundingClientRect();
        return { left:box.left, top:box.top, right:box.right, bottom:box.bottom, width:box.width, height:box.height };
      };
      const input = document.querySelector('.vq43-fallback-metrics input');
      const sampleButton = document.querySelector('.vq43-fallback-sample button');
      const style = input ? getComputedStyle(input) : null;
      return {
        options:rect('.vq43-sim-options'),
        workspace:rect('.vq43-workspace-panel'),
        actions:rect('.vq43-top-actions'),
        inputHeight:input?.getBoundingClientRect().height || 0,
        buttonHeight:sampleButton?.getBoundingClientRect().height || 0,
        inputBackground:style?.backgroundColor || '',
        koreanTime:window.__VISIONQC_DEBUG__.formatLogTime('17시 17분 4초'),
        agentTime:window.__VISIONQC_DEBUG__.formatLogTime('17:17:26.771')
      };
    });

    expect(result.options).not.toBeNull();
    expect(result.workspace).not.toBeNull();
    expect(result.actions).not.toBeNull();
    // At FHD the main workspace and Options are intentional two-column siblings;
    // only the top action rail must not be covered by Options.
    expect(result.options.top).toBeGreaterThanOrEqual(result.actions.bottom - 1);
    expect(result.inputHeight).toBe(32);
    expect(result.buttonHeight).toBe(32);
    expect(result.inputBackground).toBe('rgb(5, 13, 24)');
    expect(result.koreanTime).toBe('17:17:04.000');
    expect(result.agentTime).toBe('17:17:26.771');
  });

  test('SQLite History and AI inspection navigation render without an Agent connection', async ({ page }) => {
    await page.goto('/index.html?vqDebug=1&browserRegression=1', { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => Boolean(window.__VISIONQC_DEBUG__), null, { timeout: 15000 });

    await page.locator('[data-vq-page="main"]').click();
    await expect(page.locator('.vq43-main-history-dashboard')).toBeVisible();

    await page.locator('[data-vq-page="history"]').click();
    await expect(page.locator('.vq43-history-page')).toBeVisible();
    await expect(page.locator('[data-history-field="cellId"]')).toBeVisible();
    await expect(page.locator('.vq43-history-kpis')).toBeVisible();

    await page.locator('[data-vq-page="inspection"]').click();
    await expect(page.locator('.vq43-inspection-page')).toBeVisible();
    await expect(page.locator('#vq43-inspection-image-path')).toBeVisible();
    await expect(page.locator('#vq43-inspection-heatmap')).toBeChecked();

    const settingSvg = await page.locator('[data-vq-page="settings"] .vq43-icon-svg').count();
    expect(settingSvg).toBeGreaterThan(0);
  });
});
