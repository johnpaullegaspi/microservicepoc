import { useEffect, useState } from 'react';
import { api, errorMessage } from '../api';
import OrderView, { isFinal } from '../components/OrderView';
import type { Order } from '../types';

/** Order confirmation: polls the REST API until payment finishes. */
export default function OrderPage({ orderNumber }: { orderNumber: string }) {
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let stop = false, timer: number | undefined;
    const tick = async () => {
      try {
        const o = await api.orderByNumber(orderNumber);
        if (stop) return;
        setOrder(o); setError('');
        if (!isFinal(o)) timer = window.setTimeout(tick, 2000);
      } catch (e) { if (!stop) { setError(errorMessage(e)); timer = window.setTimeout(tick, 4000); } }
    };
    tick();
    return () => { stop = true; window.clearTimeout(timer); };
  }, [orderNumber]);

  async function cancel() {
    if (!order) return;
    setBusy(true);
    try { setOrder(await api.cancel(order.id)); } catch (e) { setError(errorMessage(e)); } finally { setBusy(false); }
  }

  if (!order) return error ? <p className="error">{error}</p> : <p className="muted">Loading order…</p>;
  const processing = !isFinal(order);
  const canCancel = order.orderStatus === 'Pending' || order.orderStatus === 'PaymentFailed';

  return (
    <>
      <h1>{order.orderStatus === 'Confirmed' ? 'Thank you! Order confirmed' : order.orderStatus === 'PaymentFailed' ? 'Payment failed' : order.orderStatus === 'Cancelled' ? 'Order cancelled' : 'Order received'}</h1>
      {processing && <div className="processing" role="status"><span className="spinner" /> Processing your payment…</div>}
      {error && <p className="error">{error}</p>}
      <OrderView order={order} />
      {canCancel && <button className="btn secondary inline" disabled={busy} onClick={cancel}>{busy ? 'Cancelling…' : 'Cancel order'}</button>}
      <a className="btn inline" href="#/">Continue shopping</a>
    </>
  );
}
