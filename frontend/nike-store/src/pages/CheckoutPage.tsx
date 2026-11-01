import { useState, type FormEvent } from 'react';
import { api, errorMessage } from '../api';
import { useCart } from '../cart';
import { go, money } from '../router';
import type { CheckoutForm } from '../types';

export default function CheckoutPage() {
  const cart = useCart();
  const [form, setForm] = useState<CheckoutForm>({ customerName: '', customerEmail: '', contactNumber: '', deliveryAddress: '', paymentMethod: 'CreditCard' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  if (cart.lines.length === 0) return <><h1>Checkout</h1><p className="muted">Your cart is empty.</p><a className="btn inline" href="#/">Shop</a></>;
  const set = (k: keyof CheckoutForm) => (e: { target: { value: string } }) => setForm(f => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true); setError('');
    try {
      const order = await api.createOrder(form, cart.lines.map(l => ({ productId: l.product.id, quantity: l.quantity })));
      cart.clear();
      go(`/order/${order.orderNumber}`);
    } catch (err) { setError(errorMessage(err)); setBusy(false); }
  }

  return (
    <div className="two-col">
      <form onSubmit={submit} className="form">
        <h1>Checkout</h1>
        <label>Full name<input required maxLength={200} value={form.customerName} onChange={set('customerName')} /></label>
        <label>Email<input required type="email" maxLength={254} value={form.customerEmail} onChange={set('customerEmail')} /></label>
        <label>Contact number<input required maxLength={30} value={form.contactNumber} onChange={set('contactNumber')} placeholder="+63 917 123 4567" /></label>
        <label>Delivery address<textarea required maxLength={500} rows={3} value={form.deliveryAddress} onChange={set('deliveryAddress')} /></label>
        <fieldset>
          <legend>Payment method (simulated)</legend>
          {[['CreditCard', 'Credit Card'], ['DebitCard', 'Debit Card'], ['CashOnDelivery', 'Cash on Delivery']].map(([v, label]) => (
            <label className="radio" key={v}><input type="radio" name="pm" checked={form.paymentMethod === v} onChange={() => setForm(f => ({ ...f, paymentMethod: v }))} /> {label}</label>
          ))}
          <p className="muted small">No card details are collected. Tip: an email containing “fail” simulates a declined payment.</p>
        </fieldset>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="btn" disabled={busy}>{busy ? 'Placing order…' : `Place order · ${money(cart.total)}`}</button>
      </form>
      <aside className="summary">
        <h2>Order summary</h2>
        {cart.lines.map(l => <div className="row" key={l.product.id}><span>{l.quantity} × {l.product.productName}</span><span>{money(l.quantity * l.product.price)}</span></div>)}
        <div className="row total"><span>Total</span><span>{money(cart.total)}</span></div>
      </aside>
    </div>
  );
}
