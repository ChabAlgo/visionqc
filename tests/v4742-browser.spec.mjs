import {test,expect} from '@playwright/test';
async function open(page){
  await page.route('http://127.0.0.1:*/api/**',route=>route.fulfill({contentType:'application/json',body:'{"ok":false}'}));
  await page.goto('/index.html?vqDebug=1&browserRegression=1');
  await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
}

test('completion choice remains on top and clickable on every page',async({page})=>{
  await open(page);
  await page.route('**/api/simulation/history-decision',route=>route.fulfill({contentType:'application/json',body:'{"ok":true,"historyDecision":"declined"}'}));
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedDashboard());
  for(const target of ['main','analysis','simulation','history','settings','classification']){
    await page.evaluate(target=>{
      const d=window.__VISIONQC_DEBUG__;d.setPage(target);
      d.offerSimulationHistorySave({running:false,historyDecision:'pending',simulationRunId:target});
    },target);
    const no=page.locator('#vq43-history-save-prompt [data-history-decision="discard"]');
    expect(await no.evaluate(el=>{const r=el.getBoundingClientRect();return el.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));})).toBe(true);
    await no.click();
    await expect(page.locator('#vq43-history-save-prompt')).toHaveCount(0);
  }
});

test('dashboard saves the exact declined run without importing another copy',async({page})=>{
  await open(page);const requests=[];
  await page.route('**/api/simulation/history-decision',route=>{
    requests.push(route.request().postDataJSON());return route.fulfill({contentType:'application/json',body:'{"ok":true,"historyDecision":"saved"}'});
  });
  await page.route('**/api/history/import',()=>{throw new Error('Exact simulation must not be imported again');});
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedDeclinedDashboard());
  await page.getByRole('button',{name:'현재 결과 DB 저장'}).click();
  await expect(page.getByRole('button',{name:'DB 저장 완료'})).toBeDisabled();
  expect(requests).toEqual([{runId:'debug-declined',decision:'save'}]);
  expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.snapshot().page)).toBe('main');
});

test('loaded CSV can be saved from dashboard without navigating away',async({page})=>{
  await open(page);const records=[];
  await page.route('**/api/history/import',route=>{
    records.push(...route.request().postDataJSON().records);
    return route.fulfill({contentType:'application/json',body:JSON.stringify({ok:true,saved:records.length})});
  });
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.seedDashboard());
  await page.getByRole('button',{name:'현재 결과 DB 저장'}).click();
  await expect(page.getByRole('button',{name:'DB 저장 완료'})).toBeDisabled();
  expect(records.length).toBeGreaterThan(0);
  expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.snapshot().page)).toBe('main');
});
