import {test,expect} from '@playwright/test';

test('Simulation start accepts an acknowledgement later than ten seconds without a second start',async({page})=>{
 let starts=0;
 await page.route('http://127.0.0.1:*/api/**',async route=>{
  if(new URL(route.request().url()).pathname==='/api/simulation/start'){
   starts++;await new Promise(resolve=>setTimeout(resolve,11000));
   await route.fulfill({json:{ok:true,state:{running:true,simulationRunId:'new-run'}}});
  }else await route.fulfill({json:{ok:false}});
 });
 await page.goto('/index.html?vqDebug=1&browserRegression=1');
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 const response=await page.evaluate(()=>window.__VISIONQC_DEBUG__.requestSimulationStart({},'old-run'));
 expect(response.state.simulationRunId).toBe('new-run');expect(starts).toBe(1);
});

for(const scenario of ['running','completed','old-run','unreachable'])test(`Lost start acknowledgement reconciles ${scenario} without retrying start`,async({page})=>{
 let starts=0;
 await page.addInitScript(()=>{const original=window.setTimeout;window.setTimeout=(fn,ms,...args)=>original(fn,ms===60000?50:ms,...args);});
 await page.route('http://127.0.0.1:*/api/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  if(path==='/api/simulation/start'){starts++;return;}
  if(path==='/api/simulation/state'){
   if(scenario==='unreachable'){await route.abort();return;}
   await route.fulfill({json:{running:scenario==='running',simulationRunId:scenario==='old-run'?'old-run':'new-run'}});return;
  }
  await route.fulfill({json:{ok:false}});
 });
 await page.goto('/index.html?vqDebug=1&browserRegression=1');
 await page.waitForFunction(()=>window.__VISIONQC_DEBUG__);
 const response=await page.evaluate(async()=>{try{return await window.__VISIONQC_DEBUG__.requestSimulationStart({},'old-run');}catch(e){return {code:e.code};}});
 if(['running','completed'].includes(scenario)){expect(response.state.simulationRunId).toBe('new-run');expect(response.state.running).toBe(scenario==='running');}
 else expect(response.code).toBe('SIMULATION_START_UNCONFIRMED');
 expect(starts).toBe(1);
});
