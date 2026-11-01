import { useCart } from '../cart';
import { go, money } from '../router';

export default function CartPage() {
  const cart = useCart();
  if (cart.lines.length === 0)
    return <><h1>Your cart</h1><p className="muted">Your cart is empty.</p><a className="btn inline" href="#/">Continue shopping</a></>;

  return (
    <>
      <h1>Your cart</h1>
      <div className="list">
        {cart.lines.map(l => (
          <div className="line" key={l.product.id}>
            <img src={l.product.imageUrl} alt="" />
            <div className="grow">
              <strong>{l.product.productName}</strong>
              <div className="muted">{money(l.product.price)} each</div>
            </div>
            <input type="number" min={1} max={Math.min(l.product.stockQuantity, 20)} value={l.quantity} aria-label="Quantity"
              onChange={e => cart.setQty(l.product.id, Number(e.target.value) || 1)} />
            <strong className="amount">{money(l.product.price * l.quantity)}</strong>
            <button className="link" onClick={() => cart.remove(l.product.id)}>Remove</button>
          </div>
        ))}
      </div>
      <div className="summary">
        <div className="row"><span>Subtotal</span><span>{money(cart.total)}</span></div>
        <div className="row total"><span>Total</span><span>{money(cart.total)}</span></div>
        <button className="btn" onClick={() => go('/checkout')}>Proceed to checkout</button>
      </div>
    </>
  );
}
