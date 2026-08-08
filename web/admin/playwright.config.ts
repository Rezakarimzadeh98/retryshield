import { defineConfig, devices } from '@playwright/test';

const isCI = Boolean((globalThis as { process?: { env: Record<string, string | undefined> } }).process?.env.CI);

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  retries: isCI ? 2 : 0,
  reporter: 'html',
  use: { baseURL: 'http://127.0.0.1:4173', trace: 'on-first-retry' },
  webServer: {
    command: 'npm run build && npm exec vite preview -- --host 127.0.0.1',
    url: 'http://127.0.0.1:4173',
    timeout: 180_000,
    reuseExistingServer: !isCI,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
