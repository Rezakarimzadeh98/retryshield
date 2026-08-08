import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from './App';

const record = {
  id: 'rec-1', tenant: 'acme', route: 'POST /payments', key: 'payment-001', fingerprint: 'sha256:abc',
  state: 'Indeterminate', error: 'upstream timeout', createdAt: '2026-08-08T08:00:00Z',
  updatedAt: '2026-08-08T08:00:02Z', expiresAt: '2026-08-09T08:00:00Z', latencyMs: 2012,
  timeline: [{ at: '2026-08-08T08:00:00Z', state: 'Processing', note: 'claimed' },
    { at: '2026-08-08T08:00:02Z', state: 'Indeterminate', note: 'upstream timeout' }],
};

function json(data: unknown) {
  return Promise.resolve(new Response(JSON.stringify(data), { status: 200, headers: { 'Content-Type': 'application/json' } }));
}

describe('admin dashboard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith('/stats')) return json({ total: 1, processing: 0, indeterminate: 1, completedRate: 91.5 });
      if (url.endsWith('/rec-1')) return json(record);
      if (url.endsWith('/resolve')) return json({ ...record, state: JSON.parse(String(init?.body)).state, error: null });
      return json([record]);
    }));
  });
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it('stores the token in session storage and loads records', async () => {
    render(<App />);
    await userEvent.type(screen.getByLabelText('Bearer token'), 'secret-token');
    await userEvent.click(screen.getByRole('button', { name: /open console/i }));
    expect(sessionStorage.getItem('retryshield.adminToken')).toBe('secret-token');
    expect(await screen.findByText('payment-001')).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith('/api/admin/stats',
      expect.objectContaining({ headers: expect.objectContaining({ Authorization: 'Bearer secret-token' }) }));
  });

  it('filters by status and searches after debounce', async () => {
    sessionStorage.setItem('retryshield.adminToken', 'token');
    render(<App />);
    await screen.findByText('payment-001');
    await userEvent.click(screen.getByRole('button', { name: 'Indeterminate' }));
    fireEvent.change(screen.getByLabelText('Search records'), { target: { value: 'acme' } });
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('search=acme'), expect.anything()), { timeout: 1000 });
  });

  it('opens details and confirms resolution', async () => {
    sessionStorage.setItem('retryshield.adminToken', 'token');
    render(<App />);
    await userEvent.click(await screen.findByRole('button', { name: 'View payment-001' }));
    expect(await screen.findByText('Resolution required')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /mark completed/i }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /confirm completed/i }));
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/resolve'),
      expect.objectContaining({ method: 'POST' })));
  });
});
