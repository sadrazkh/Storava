import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './ClientApp/e2e',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:5120',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'dotnet run --no-launch-profile --urls http://127.0.0.1:5120',
    url: 'http://127.0.0.1:5120/health',
    reuseExistingServer: !process.env.CI,
    cwd: '.',
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
    },
    timeout: 120_000,
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        channel: process.env.CI ? undefined : 'chrome',
      },
    },
  ],
});
