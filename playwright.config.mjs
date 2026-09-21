import { defineConfig } from '@playwright/test';
const baseURL = `http://127.0.0.1:${Number(process.env.VISIONQC_TEST_PORT || 4173)}`;

export default defineConfig({
  testDir: './tests',
  testMatch: ['v482-browser.spec.mjs', 'v481-browser.spec.mjs', 'v4749-browser.spec.mjs', 'v4746-browser.spec.mjs', 'v4745-browser.spec.mjs', 'v4744-browser.spec.mjs', 'v4743-browser.spec.mjs', 'v4742-browser.spec.mjs', 'v4741-browser.spec.mjs', 'v4740-browser.spec.mjs', 'v4739-browser.spec.mjs', 'v4733-browser.spec.mjs', 'v4731-browser.spec.mjs', 'v4725-browser.spec.mjs', 'v4722-browser.spec.mjs', 'v4720-browser.spec.mjs', 'browser-regression.spec.mjs', 'v4712-browser.spec.mjs', 'v4713-browser.spec.mjs', 'v4714-browser.spec.mjs', 'v4715-browser.spec.mjs'],
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['line'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
  use: {
    baseURL,
    browserName: 'chromium',
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure'
  },
  webServer: {
    command: 'node tests/static-server.mjs',
    url: `${baseURL}/index.html`,
    reuseExistingServer: !process.env.CI,
    timeout: 120000
  }
});
