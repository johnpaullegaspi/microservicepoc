import { useEffect, useState, type FormEvent } from 'react';
import { api, errorMessage } from '../api';
import OrderView, { isFinal } from '../components/OrderView';
import type { Order } from '../types';

/** Track by order number or e-mail. Auto-refreshes while any order is still in flight. */
export default function TrackPage({ initial }: { initial: string }) {
  const [query, setQuery] = useState(initial);
  const [active, setActive] = useState(initial);
  const [orders, setOrders] = useState<Order[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!active) return;
    let stop = false, timer: number | undefined;
    const load = async (first: boolean) => {
      if (first) setLoading(true);
      try {
        const q = active.trim();
        const result = q.includes('@') ? await api.ordersByEmail(q) : [await api.orderByNumber(q)];
        if (stop) return;
        setOrders(result); setError('');
        if (result.some(o => !isFinal(o))) timer = window.setTimeout(() => load(false), 3000);
      } catch (e) { if (!stop) { setOrders([]); setError(errorMessage(e)); } }
      finally { if (!stop) setLoading(false); }
    };
    load(true);
    return () => { stop = true; window.clearTimeout(timer); };
  }, [active]);

  const submit = (e: FormEvent) => { e.preventDefault(); setActive(query); };

  return (
    <>
      <h1>Track your order</h1>
      <form onSubmit={submit} className="search">
        <input value={query} onChange={e => setQuery(e.target.value)} placeholder="Order number (ORD-…) or email" required />
        <button className="btn">Track</button>
      </form>
      {loading && <p className="muted">Searching…</p>}
      {error && <p className="error">{error}</p>}
      {!loading && !error && active && orders.length === 0 && <p className="muted">No orders found.</p>}
      {orders.map(o => <OrderView key={o.id} order={o} />)}
    </>
  );
}
