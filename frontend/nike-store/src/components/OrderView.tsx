import { money } from '../router';
import type { Order } from '../types';

const badge = (s: string) => {
  const good = ['Confirmed', 'Successful'], bad = ['PaymentFailed', 'Failed', 'Cancelled'];
  return <span className={`badge ${good.includes(s) ? 'ok' : bad.includes(s) ? 'bad' : 'wait'}`}>{s}</span>;
};

export const isFinal = (o: Order) => ['Confirmed', 'PaymentFailed', 'Cancelled'].includes(o.orderStatus);

export default function OrderView({ order }: { order: Order }) {
  return (
    <section className="panel">
      <div className="row"><h2>{order.orderNumber}</h2><span className="muted small">{new Date(order.createdAt).toLocaleString()}</span></div>
      <div className="status-row">
        <div>Order status {badge(order.orderStatus)}</div>
        <div>Payment status {badge(order.paymentStatus)}</div>
      </div>
      {order.transactionReference && <p>Transaction reference: <code>{order.transactionReference}</code></p>}
      {order.failureReason && <p className="error">Payment issue: {order.failureReason}</p>}
      <h3>Customer</h3>
      <p>{order.customerName} · {order.customerEmail} · {order.contactNumber}<br />{order.deliveryAddress}</p>
      <h3>Items</h3>
      {order.items.map(i => <div className="row" key={i.productId}><span>{i.quantity} × {i.productName}</span><span>{money(i.subtotal)}</span></div>)}
      <div className="row total"><span>Total ({order.paymentMethod})</span><span>{money(order.totalAmount)}</span></div>
    </section>
  );
}
