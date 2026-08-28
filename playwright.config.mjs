import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  testMatch: ['browser-regression.spec.mjs', 'v4712-browser.spec.mjs', 'v4713-browser.spec.mjs', 'v4714-browser.spec.mjs'],
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['line'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:4173',
    browserName: 'chromium',
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure'
  },
  webServer: {
    command: 'node tests/static-server.mjs',
    url: 'http://127.0.0.1:4173/index.html',
    reuseExistingServer: !process.env.CI,
    timeout: 120000
  }
});
