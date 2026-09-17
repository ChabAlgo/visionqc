import { test, expect } from '@playwright/test';

test('date chart centers fixed slots, pages after ten, and only joins adjacent dots', async ({page}) => {
  await page.goto('/index.html?vqDebug=1&browserRegression=1');
  await page.waitForFunction(() => window.__VISIONQC_DEBUG__);
  for (const count of [1, 2, 3, 10, 11, 31]) {
    await page.evaluate(count => window.__VISIONQC_DEBUG__.seedRows(Array.from({length:count},(_,i)=>({
      cellId:'CELL'+i, position:'AN(TOP)', captureTimestamp:`2026-01-${String(i+1).padStart(2,'0')}T08:00:00`, totalResult:'NG', tools:{}
    }))), count);
    const chart=page.locator('.vq43-main-history-dashboard .vq43-history-line');
    await expect(chart.locator('circle')).toHaveCount(Math.min(count,10));
    await expect(chart.locator('.vq43-history-segment')).toHaveCount(Math.min(count,10)-1);
    const geometry=await chart.evaluate(svg=>{
      const parent=svg.parentElement, dots=[...svg.querySelectorAll('circle')];
      const xs=dots.map(dot=>parseFloat(dot.getAttribute('cx')));
      const segments=[...svg.querySelectorAll('.vq43-history-segment')];
      return {xs, font:parseFloat(getComputedStyle(svg.querySelector('text.rate')).fontSize),
        scroll:parent.scrollWidth>parent.clientWidth+2,
        endpoints:segments.every((line,i)=>parseFloat(line.getAttribute('x1'))===xs[i]&&parseFloat(line.getAttribute('x2'))===xs[i+1])};
    });
    expect(geometry.font).toBeGreaterThanOrEqual(15);
    expect(geometry.endpoints).toBe(true);
    if(count<=10) {
      expect((geometry.xs[0]+geometry.xs.at(-1))/2).toBeCloseTo(50);
      if(count>1) expect(geometry.xs[1]-geometry.xs[0]).toBeCloseTo(10);
    } else expect(geometry.scroll).toBe(false);
    if(count===3 || count===11) await page.locator('.vq43-main-history-dashboard').screenshot({path:`test-results/date-chart-${count}.png`});
    if(count===31) {
      await chart.locator('.vq43-history-point').last().click();
      await expect(chart.locator('circle')).toHaveCount(10);
      expect((await page.evaluate(()=>window.__VISIONQC_DEBUG__.dateSnapshot())).selected).toBe('2026-01-31');
    }
  }
});

test('Tool OK in a detected cell is excluded from the actual missed-cell scope', async ({page}) => {
  await page.goto('/index.html?vqDebug=1&browserRegression=1');
  await page.waitForFunction(() => window.__VISIONQC_DEBUG__);
  const result=await page.evaluate(()=>{
    window.__VISIONQC_DEBUG__.seedAnalysis();
    return window.__VISIONQC_DEBUG__.missedScoreSnapshot('FoilDamage');
  });
  expect(result.misses).toHaveLength(1);
  expect(result.points).toHaveLength(1);
  expect(result.points.map(p=>p.recordKey)).toEqual(result.misses);
});
