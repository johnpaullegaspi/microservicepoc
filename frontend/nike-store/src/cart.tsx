import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { CartLine, Product } from './types';

interface CartApi {
  lines: CartLine[]; count: number; total: number;
  add: (p: Product) => void; setQty: (id: string, qty: number) => void; remove: (id: string) => void; clear: () => void;
}
const Ctx = createContext<CartApi>(null!);
export const useCart = () => useContext(Ctx);

const KEY = 'nike-store-cart';
const load = (): CartLine[] => { try { return JSON.parse(localStorage.getItem(KEY) ?? '[]'); } catch { return []; } };

export function CartProvider({ children }: { children: ReactNode }) {
  const [lines, setLines] = useState<CartLine[]>(load);
  useEffect(() => { try { localStorage.setItem(KEY, JSON.stringify(lines)); } catch { /* ignore */ } }, [lines]);

  const api = useMemo<CartApi>(() => ({
    lines,
    count: lines.reduce((n, l) => n + l.quantity, 0),
    total: lines.reduce((n, l) => n + l.quantity * l.product.price, 0),
    add: p => setLines(ls => {
      const ex = ls.find(l => l.product.id === p.id);
      if (ex) return ls.map(l => l.product.id === p.id ? { ...l, quantity: Math.min(l.quantity + 1, p.stockQuantity, 20) } : l);
      return [...ls, { product: p, quantity: 1 }];
    }),
    setQty: (id, qty) => setLines(ls => ls.map(l => l.product.id === id
      ? { ...l, quantity: Math.max(1, Math.min(qty, l.product.stockQuantity, 20)) } : l)),
    remove: id => setLines(ls => ls.filter(l => l.product.id !== id)),
    clear: () => setLines([]),
  }), [lines]);

  return <Ctx.Provider value={api}>{children}</Ctx.Provider>;
}
