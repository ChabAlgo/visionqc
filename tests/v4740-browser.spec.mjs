import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';

const csv = 'CaptureTimestamp,Cell ID,Position,Total_result,FullPath,WorkspaceType,WorkspaceName,WorkspaceKey,Crack_result,Crack_score\r\n' +
  '2026-02-03T08:00:00,P163GG22M2100001,AN(TOP),NG,C:\\first\\same.jpg,green,Workspace A,green-a,NG,0.6000123456789\r\n' +
  '2026-02-03T09:00:00,P163GG22M2100001,AN(TOP),NG,C:\\second\\same.jpg,green,Workspace A,green-a,NG,0.9000987654321\r\n';

async function open(page) {
  await page.goto('/index.html?vqDebug=1&browserRegression=1');
  await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
}

test('same Cell observations keep their exact source image and score through graph navigation', async ({page})=>{
  await open(page);
  await page.evaluate(text=>window.__VISIONQC_DEBUG__.seedCsv(text),csv);
  const second=await page.evaluate(()=>window.__VISIONQC_DEBUG__.openIntegrityPoint(1));
  expect(second).toEqual({path:'C:\\second\\same.jpg',score:.9000987654321,images:1});
  await expect(page.locator('.vq43-modal-scores')).toContainText('0.9001');
  await page.keyboard.press('ArrowLeft');
  await expect(page.locator('.vq43-modal-scores')).toContainText('0.6000');
  await expect(page.locator('[data-vq-action="modal-prev"]')).toBeDisabled();
});

test('full export roundtrip preserves all raw rows, precise scores, paths, workspace and daily totals', async ({page})=>{
  await open(page);
  await page.evaluate(text=>window.__VISIONQC_DEBUG__.seedCsv(text),csv);
  const before=await page.evaluate(()=>({rows:window.__VISIONQC_DEBUG__.integrityRows(),dates:window.__VISIONQC_DEBUG__.dateSnapshot()}));
  await page.evaluate(()=>window.showSaveFilePicker=undefined);
  const downloadPromise=page.waitForEvent('download');
  await page.evaluate(()=>window.__VISIONQC_DEBUG__.exportAllResults());
  const exported=await readFile(await (await downloadPromise).path(),'utf8');
  await page.evaluate(text=>window.__VISIONQC_DEBUG__.seedCsv(text),exported);
  const after=await page.evaluate(()=>({rows:window.__VISIONQC_DEBUG__.integrityRows(),dates:window.__VISIONQC_DEBUG__.dateSnapshot()}));
  const observations=rows=>rows.map(({sourceFileName,sourceRowNumber,...row})=>row);
  expect(observations(after.rows)).toEqual(observations(before.rows));
  expect(after.rows).toHaveLength(2);
  expect(after.dates).toEqual(before.dates);
});

test('decimal comma is a fraction and invalid score refuses import without replacing current data',async({page})=>{
  await open(page);
  await page.evaluate(text=>window.__VISIONQC_DEBUG__.seedCsv(text),csv.replace('0.6000123456789','"0,75"'));
  expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.integrityRows()[0].tools.Crack.score)).toBe(.75);
  const error=await page.evaluate(async text=>{try{await window.__VISIONQC_DEBUG__.seedCsv(text);return '';}catch(e){return e.message;}},csv.replace('0.6000123456789','75'));
  expect(error).toContain('Score');
  expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.integrityRows()[0].tools.Crack.score)).toBe(.75);
});

test('actual NG folder retains every date and image of a repeated Cell without reading image bytes',async({page})=>{
  await open(page);
  await page.evaluate(text=>window.__VISIONQC_DEBUG__.seedCsv(text),csv);
  const result=await page.evaluate(async()=>{
    const names=['20260203_080000_P163GG22M2100001.jpg','20260204_080000_P163GG22M2100001.jpg','20260204_090000_P163GG22M2100001.jpg'];
    const folder={name:'NG',async *entries(){for(const name of names)yield [name,{kind:'file',getFile(){throw new Error('Image bytes must remain lazy');}}];}};
    return (await window.__VISIONQC_DEBUG__.scanIntegrityFolder(folder,'AN(TOP)',false)).images.map(i=>i.relativePath);
  });
  expect(result).toHaveLength(3);
  expect(result[1]).toContain('20260204');
});

test('malformed CSV rows, unfinished quotes and duplicate headers leave loaded results intact',async({page})=>{
  await open(page);
  await page.evaluate(text=>window.__VISIONQC_DEBUG__.seedCsv(text),csv);
  for (const invalid of [csv+'short,row\r\n', csv+'"unfinished', csv.replace('WorkspaceType','Position')]) {
    const error=await page.evaluate(async text=>{try{await window.__VISIONQC_DEBUG__.seedCsv(text);return '';}catch(e){return e.message;}},invalid);
    expect(error).not.toBe('');
    expect(await page.evaluate(()=>window.__VISIONQC_DEBUG__.integrityRows().length)).toBe(2);
  }
});
