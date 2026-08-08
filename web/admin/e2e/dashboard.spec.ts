import { expect, test } from '@playwright/test';

test('authenticates and renders the operations dashboard', async ({ page }) => {
  const record = {
    id: 'rec-smoke', tenant: 'acme', route: 'POST /payments', key: 'smoke-payment',
    fingerprint: 'sha256:smoke', state: 'Completed', createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(), expiresAt: new Date(Date.now() + 86400000).toISOString(),
    latencyMs: 84, timeline: [], response: { statusCode: 201, headers: {}, body: '{"ok":true}' },
  };
  await page.route('**/api/admin/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/stats')) await route.fulfill({ json: { total: 1, processing: 0, indeterminate: 0, completedRate: 100 } });
    else if (path.endsWith('/rec-smoke')) await route.fulfill({ json: record });
    else await route.fulfill({ json: [record] });
  });
  await page.goto('/');
  await page.getByLabel('Bearer token').fill('smoke-token');
  await page.getByRole('button', { name: /open console/i }).click();
  await expect(page.getByRole('heading', { name: 'Idempotency records' })).toBeVisible();
  await expect(page.getByText('smoke-payment')).toBeVisible();
  await page.getByRole('button', { name: 'View smoke-payment' }).click();
  await expect(page.getByRole('complementary', { name: 'Record details' })).toBeVisible();
  await expect(page.getByText('HTTP status')).toBeVisible();
});
