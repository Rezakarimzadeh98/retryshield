export type RecordStatus = 'Processing' | 'Completed' | 'Failed' | 'Indeterminate' | 'Expired';

export interface RecordEvent {
  at: string;
  state: RecordStatus;
  note?: string | null;
}

export interface StoredResponse {
  statusCode: number;
  headers: Record<string, string[]>;
  body?: string;
}

export interface IdempotencyRecord {
  id: string;
  tenant: string;
  route: string;
  key: string;
  fingerprint: string;
  state: RecordStatus;
  error?: string | null;
  createdAt: string;
  updatedAt: string;
  expiresAt: string;
  latencyMs?: number;
  response?: StoredResponse | null;
  timeline: RecordEvent[];
}

export interface DashboardStats {
  total: number;
  processing: number;
  indeterminate: number;
  completedRate: number;
}

export interface RecordQuery {
  search?: string;
  status?: string;
}

const configuredUrl = import.meta.env.VITE_ADMIN_API_URL?.trim();
export const API_URL = (configuredUrl || 'http://localhost:8081').replace(/\/$/, '');
const TOKEN_KEY = 'retryshield.adminToken';

export const tokenStore = {
  get: () => sessionStorage.getItem(TOKEN_KEY),
  set: (token: string) => sessionStorage.setItem(TOKEN_KEY, token),
  clear: () => sessionStorage.removeItem(TOKEN_KEY),
};

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = tokenStore.get();
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(body || `${response.status} ${response.statusText}`);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export const adminApi = {
  stats: () => request<DashboardStats>('/api/admin/stats'),
  records: (query: RecordQuery = {}) => {
    const params = new URLSearchParams();
    if (query.search) params.set('search', query.search);
    if (query.status && query.status !== 'All') params.set('status', query.status);
    const suffix = params.size ? `?${params}` : '';
    return request<IdempotencyRecord[]>(`/api/admin/records${suffix}`);
  },
  record: (id: string) => request<IdempotencyRecord>(`/api/admin/records/${encodeURIComponent(id)}`),
  resolve: (id: string, state: 'Completed' | 'Failed') =>
    request<IdempotencyRecord>(`/api/admin/records/${encodeURIComponent(id)}/resolve`, {
      method: 'POST',
      body: JSON.stringify({ state }),
    }),
  purgeExpired: () => request<{ purged: number }>('/api/admin/records/expired', { method: 'DELETE' }),
};
