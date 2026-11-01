import { useCart } from './cart';
import { go, useRoute } from './router';
import ProductsPage from './pages/ProductsPage';
import CartPage from './pages/CartPage';
import CheckoutPage from './pages/CheckoutPage';
import OrderPage from './pages/OrderPage';
import TrackPage from './pages/TrackPage';

export default function App() {
  const route = useRoute();
  const { count } = useCart();
  const [, section, arg] = route.split('/');

  let page;
  switch (section) {
    case 'cart': page = <CartPage />; break;
    case 'checkout': page = <CheckoutPage />; break;
    case 'order': page = <OrderPage orderNumber={decodeURIComponent(arg ?? '')} />; break;
    case 'track': page = <TrackPage initial={decodeURIComponent(arg ?? '')} />; break;
    default: page = <ProductsPage />;
  }

  return (
    <>
      <header className="nav">
        <a className="brand" href="#/">NIKE<span> STORE</span></a>
        <nav>
          <a href="#/">Shop</a>
          <a href="#/track">Track order</a>
          <button className="cart-btn" onClick={() => go('/cart')} aria-label="Open cart">Cart ({count})</button>
        </nav>
      </header>
      <main className="container">{page}</main>
      <footer className="footer">Proof-of-concept store · simulated payments only · no card data is collected</footer>
    </>
  );
}
