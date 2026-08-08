import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import {
  Activity, AlertTriangle, ArchiveX, Check, CheckCircle2, ChevronRight, CircleDot,
  Clock3, Copy, Database, KeyRound, LogOut, RefreshCw, Search, ShieldCheck, X, XCircle,
} from 'lucide-react';
import { adminApi, API_URL, DashboardStats, IdempotencyRecord, RecordStatus, tokenStore } from './api';

const statuses: Array<'All' | RecordStatus> = ['All', 'Processing', 'Completed', 'Failed', 'Indeterminate', 'Expired'];

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value));
}

function StatusBadge({ status }: { status: RecordStatus }) {
  const Icon = status === 'Completed' ? CheckCircle2 : status === 'Failed' ? XCircle :
    status === 'Indeterminate' ? AlertTriangle : status === 'Processing' ? Activity : Clock3;
  return <span className={`badge badge-${status.toLowerCase()}`}><Icon size={13} />{status}</span>;
}

function Login({ onLogin }: { onLogin: () => void }) {
  const [token, setToken] = useState('');
  function submit(event: FormEvent) {
    event.preventDefault();
    if (!token.trim()) return;
    tokenStore.set(token.trim());
    onLogin();
  }
  return (
    <main className="login-shell">
      <section className="login-panel" aria-labelledby="login-title">
        <div className="brand-mark"><ShieldCheck aria-hidden="true" /></div>
        <p className="eyebrow">RetryShield operations</p>
        <h1 id="login-title">Admin access</h1>
        <p className="muted">Enter an admin bearer token to inspect and resolve idempotency records.</p>
        <form onSubmit={submit}>
          <label htmlFor="token">Bearer token</label>
          <div className="token-field"><KeyRound size={18} /><input id="token" autoFocus type="password" value={token}
            onChange={(event) => setToken(event.target.value)} placeholder="Paste token" autoComplete="off" /></div>
          <button className="button primary full" type="submit" disabled={!token.trim()}>Open console <ChevronRight size={17} /></button>
        </form>
        <p className="endpoint">API endpoint <code>{API_URL}</code></p>
      </section>
    </main>
  );
}

function StatCard({ label, value, detail, icon: Icon, tone }: {
  label: string; value: string | number; detail: string; icon: typeof Database; tone: string;
}) {
  return <article className={`stat-card ${tone}`}>
    <div className="stat-top"><span>{label}</span><Icon size={18} /></div>
    <strong>{value}</strong><small>{detail}</small>
  </article>;
}

function RecordDrawer({ record, loading, onClose, onResolve }: {
  record: IdempotencyRecord | null; loading: boolean; onClose: () => void;
  onResolve: (state: 'Completed' | 'Failed') => Promise<void>;
}) {
  const [confirm, setConfirm] = useState<'Completed' | 'Failed' | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => setConfirm(null), [record?.id]);
  if (!record && !loading) return null;
  async function resolve() {
    if (!confirm) return;
    setBusy(true);
    try { await onResolve(confirm); setConfirm(null); } finally { setBusy(false); }
  }
  return <>
    <button className="drawer-scrim" aria-label="Close record details" onClick={onClose} />
    <aside className="drawer" aria-label="Record details" aria-live="polite">
      <div className="drawer-head">
        <div><p className="eyebrow">Record details</p><h2>{record?.key || 'Loading…'}</h2></div>
        <button className="icon-button" onClick={onClose} aria-label="Close"><X /></button>
      </div>
      {loading || !record ? <div className="drawer-loading"><span className="spinner" />Loading record…</div> :
      <div className="drawer-content">
        <div className="detail-status"><StatusBadge status={record.state} />
          <span>Updated {formatDate(record.updatedAt)}</span></div>
        {record.state === 'Indeterminate' && <section className="resolution">
          <div><AlertTriangle size={20} /><div><h3>Resolution required</h3>
            <p>Verify the upstream result before changing this final state.</p></div></div>
          <div className="resolution-actions">
            <button className="button success" onClick={() => setConfirm('Completed')}><Check size={16} />Mark completed</button>
            <button className="button danger" onClick={() => setConfirm('Failed')}><X size={16} />Mark failed</button>
          </div>
        </section>}
        {record.error && <div className="error-note"><AlertTriangle size={16} /><span>{record.error}</span></div>}
        <section><h3>Request</h3><dl className="details-grid">
          <div><dt>Tenant</dt><dd>{record.tenant}</dd></div><div><dt>Route</dt><dd>{record.route}</dd></div>
          <div><dt>Created</dt><dd>{formatDate(record.createdAt)}</dd></div>
          <div><dt>Latency</dt><dd>{record.latencyMs == null ? '—' : `${record.latencyMs} ms`}</dd></div>
          <div className="wide"><dt>Fingerprint</dt><dd className="mono">{record.fingerprint}</dd></div>
          <div className="wide"><dt>Expires</dt><dd>{formatDate(record.expiresAt)}</dd></div>
        </dl></section>
        <section><h3>Timeline</h3><ol className="timeline">
          {record.timeline?.map((event, index) => <li key={`${event.at}-${index}`}>
            <span className={`timeline-dot dot-${event.state.toLowerCase()}`} />
            <div><div><strong>{event.state}</strong><time>{formatDate(event.at)}</time></div>
              {event.note && <p>{event.note}</p>}</div>
          </li>)}
        </ol></section>
        <section><h3>Stored response</h3>
          {!record.response ? <p className="muted">No response captured.</p> : <div className="response-card">
            <div><span>HTTP status</span><strong>{record.response.statusCode}</strong></div>
            <details><summary>Headers</summary><pre>{JSON.stringify(record.response.headers, null, 2)}</pre></details>
            {record.response.body && <details><summary>Body</summary><pre>{record.response.body}</pre></details>}
          </div>}
        </section>
      </div>}
    </aside>
    {confirm && <div className="modal-wrap" role="dialog" aria-modal="true" aria-labelledby="confirm-title">
      <div className="modal"><div className={`confirm-icon ${confirm.toLowerCase()}`}>
        {confirm === 'Completed' ? <CheckCircle2 /> : <XCircle />}</div>
        <h2 id="confirm-title">Mark as {confirm.toLowerCase()}?</h2>
        <p>This records an explicit administrative resolution and cannot be undone.</p>
        <div className="modal-actions"><button className="button ghost" onClick={() => setConfirm(null)} disabled={busy}>Cancel</button>
          <button className={`button ${confirm === 'Completed' ? 'success' : 'danger'}`} onClick={resolve} disabled={busy}>
            {busy ? 'Resolving…' : `Confirm ${confirm.toLowerCase()}`}</button></div>
      </div>
    </div>}
  </>;
}

export default function App() {
  const [authenticated, setAuthenticated] = useState(() => Boolean(tokenStore.get()));
  const [records, setRecords] = useState<IdempotencyRecord[]>([]);
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [status, setStatus] = useState<(typeof statuses)[number]>('All');
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [selected, setSelected] = useState<IdempotencyRecord | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [purging, setPurging] = useState(false);
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    if (!authenticated) return;
    setLoading(true); setError('');
    try {
      const [nextStats, nextRecords] = await Promise.all([adminApi.stats(), adminApi.records({ search: query, status })]);
      setStats(nextStats); setRecords(nextRecords);
    } catch (err) { setError(err instanceof Error ? err.message : 'Unable to reach the admin API.'); }
    finally { setLoading(false); }
  }, [authenticated, query, status]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);

  async function openRecord(id: string) {
    setDetailLoading(true); setSelected(records.find((item) => item.id === id) || null);
    try { setSelected(await adminApi.record(id)); }
    catch (err) { setError(err instanceof Error ? err.message : 'Unable to load record.'); }
    finally { setDetailLoading(false); }
  }
  async function resolve(state: 'Completed' | 'Failed') {
    if (!selected) return;
    try {
      const updated = await adminApi.resolve(selected.id, state);
      setSelected(updated); setRecords((items) => items.map((item) => item.id === updated.id ? updated : item));
      setNotice(`Record marked ${state.toLowerCase()}.`); await load();
    } catch (err) { setError(err instanceof Error ? err.message : 'Resolution failed.'); throw err; }
  }
  async function purge() {
    if (!window.confirm('Permanently purge all expired records?')) return;
    setPurging(true);
    try { const result = await adminApi.purgeExpired(); setNotice(`Purged ${result.purged} expired record${result.purged === 1 ? '' : 's'}.`); await load(); }
    catch (err) { setError(err instanceof Error ? err.message : 'Purge failed.'); }
    finally { setPurging(false); }
  }
  function logout() { tokenStore.clear(); setAuthenticated(false); setRecords([]); setStats(null); }

  const visibleRecords = useMemo(() => records, [records]);
  if (!authenticated) return <Login onLogin={() => setAuthenticated(true)} />;
  return <div className="app-shell">
    <header className="topbar">
      <div className="brand"><div className="brand-mark small"><ShieldCheck /></div><div><strong>RetryShield</strong><span>Operations console</span></div></div>
      <div className="top-actions"><span className="health"><CircleDot size={14} />Admin API</span>
        <button className="icon-button" onClick={logout} aria-label="Sign out" title="Sign out"><LogOut /></button></div>
    </header>
    <main className="content">
      <div className="page-head"><div><p className="eyebrow">Control plane</p><h1>Idempotency records</h1>
        <p>Monitor request outcomes and resolve uncertain operations.</p></div>
        <div className="page-actions"><button className="button ghost" onClick={() => void load()} disabled={loading}>
          <RefreshCw size={16} className={loading ? 'spin' : ''} />Refresh</button>
          <button className="button danger-subtle" onClick={() => void purge()} disabled={purging}><ArchiveX size={16} />{purging ? 'Purging…' : 'Purge expired'}</button></div>
      </div>
      {notice && <div className="notice" role="status"><CheckCircle2 size={17} />{notice}<button onClick={() => setNotice('')} aria-label="Dismiss"><X size={15} /></button></div>}
      <section className="stats-grid" aria-label="Record overview">
        <StatCard label="Total records" value={stats?.total ?? '—'} detail="Across all tenants" icon={Database} tone="blue" />
        <StatCard label="Processing" value={stats?.processing ?? '—'} detail="Currently in flight" icon={Activity} tone="violet" />
        <StatCard label="Needs resolution" value={stats?.indeterminate ?? '—'} detail="Indeterminate outcomes" icon={AlertTriangle} tone="amber" />
        <StatCard label="Completion rate" value={stats ? `${stats.completedRate.toFixed(1)}%` : '—'} detail="Successful outcomes" icon={CheckCircle2} tone="green" />
      </section>
      <section className="records-panel">
        <div className="filters"><div className="search-field"><Search size={17} /><label className="sr-only" htmlFor="search">Search records</label>
          <input id="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search key, tenant, route…" /></div>
          <div className="status-tabs" role="group" aria-label="Filter by status">{statuses.map((item) =>
            <button key={item} className={status === item ? 'active' : ''} onClick={() => setStatus(item)}>{item}</button>)}</div>
        </div>
        {error && <div className="state-message error-state" role="alert"><AlertTriangle /><div><strong>Couldn’t load records</strong><p>{error}</p></div>
          <button className="button ghost" onClick={() => void load()}>Try again</button></div>}
        {!error && loading ? <div className="state-message"><span className="spinner" /><div><strong>Loading records</strong><p>Contacting {API_URL}</p></div></div> :
        !error && visibleRecords.length === 0 ? <div className="empty-state"><Database /><h2>No records found</h2><p>Try adjusting the filters or search query.</p></div> :
        !error && <div className="table-wrap"><table><thead><tr><th>Record key</th><th>Tenant / route</th><th>Status</th><th>Latency</th><th>Updated</th><th><span className="sr-only">Actions</span></th></tr></thead>
          <tbody>{visibleRecords.map((record) => <tr key={record.id} onClick={() => void openRecord(record.id)}>
            <td><button className="record-key" onClick={(event) => { event.stopPropagation(); navigator.clipboard?.writeText(record.key); }} title="Copy key">
              <span>{record.key}</span><Copy size={13} /></button><small>{record.id}</small></td>
            <td><strong>{record.tenant}</strong><small>{record.route}</small></td><td><StatusBadge status={record.state} /></td>
            <td className="mono">{record.latencyMs == null ? '—' : `${record.latencyMs} ms`}</td>
            <td><time>{formatDate(record.updatedAt)}</time></td><td><button className="row-action" aria-label={`View ${record.key}`} onClick={(event) => { event.stopPropagation(); void openRecord(record.id); }}><ChevronRight /></button></td>
          </tr>)}</tbody></table></div>}
      </section>
    </main>
    <RecordDrawer record={selected} loading={detailLoading} onClose={() => setSelected(null)} onResolve={resolve} />
  </div>;
}
