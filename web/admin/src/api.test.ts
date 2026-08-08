import { afterEach, describe, expect, it, vi } from 'vitest';

afterEach(() => {
  vi.unstubAllEnvs();
  vi.resetModules();
});

describe('admin API URL', () => {
  it('uses the same-origin API path by default', async () => {
    vi.stubEnv('VITE_ADMIN_API_URL', '');

    const { API_URL } = await import('./api');

    expect(API_URL).toBe('/api');
  });

  it('supports an explicit development API origin', async () => {
    vi.stubEnv('VITE_ADMIN_API_URL', 'http://localhost:8081/');

    const { API_URL } = await import('./api');

    expect(API_URL).toBe('http://localhost:8081/api');
  });
});
