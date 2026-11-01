import { useEffect, useState } from 'react';
import { api, errorMessage } from '../api';
import { useCart } from '../cart';
import { money } from '../router';
import type { Product } from '../types';

export default function ProductsPage() {
  const [products, setProducts] = useState<Product[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const cart = useCart();

  useEffect(() => {
    api.products().then(setProducts).catch(e => setError(errorMessage(e))).finally(() => setLoading(false));
  }, []);

  if (loading) return <p className="muted">Loading shoes…</p>;
  if (error) return <p className="error">Could not load products: {error}</p>;

  return (
    <>
      <h1 className="hero">Just pick<br />a pair.</h1>
      <div className="grid">
        {products.map(p => {
          const inCart = cart.lines.find(l => l.product.id === p.id)?.quantity ?? 0;
          const soldOut = p.stockQuantity <= 0;
          return (
            <article className="card" key={p.id}>
              <img src={p.imageUrl} alt={p.productName} />
              <div className="card-body">
                <h2>{p.productName}</h2>
                <p className="muted">{p.description}</p>
                <div className="row">
                  <strong>{money(p.price)}</strong>
                  <span className={p.stockQuantity < 10 ? 'low' : 'muted'}>{soldOut ? 'Sold out' : `${p.stockQuantity} in stock`}</span>
                </div>
                <button className="btn" disabled={soldOut || inCart >= p.stockQuantity} onClick={() => cart.add(p)}>
                  {inCart ? `Add another (${inCart} in cart)` : 'Add to Cart'}
                </button>
              </div>
            </article>
          );
        })}
      </div>
    </>
  );
}
