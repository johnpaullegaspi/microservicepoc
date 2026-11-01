import axios from 'axios';
import type { CheckoutForm, Order, Product } from './types';

// Configurable base URL; default "/api" is proxied to the gateway (nginx in Docker, Vite proxy in dev).
const http = axios.create({ baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api', timeout: 20000 });

export function errorMessage(e: unknown): string {
  if (axios.isAxiosError(e)) {
    const d = e.response?.data as { title?: string; errors?: Record<string, string[]> } | undefined;
    if (d?.errors) return Object.values(d.errors).flat().join(' ');
    if (d?.title) return d.title;
    if (e.response?.status === 404) return 'Not found.';
    return e.message;
  }
  return 'Something went wrong.';
}

export const api = {
  products: () => http.get<Product[]>('/products').then(r => r.data),
  createOrder: (form: CheckoutForm, items: { productId: string; quantity: number }[]) =>
    http.post<Order>('/orders', { ...form, items }).then(r => r.data),
  orderByNumber: (n: string) => http.get<Order>(`/orders/number/${encodeURIComponent(n.trim())}`).then(r => r.data),
  ordersByEmail: (email: string) => http.get<Order[]>(`/orders/customer/${encodeURIComponent(email.trim())}`).then(r => r.data),
  cancel: (id: string) => http.post<Order>(`/orders/${id}/cancel`, { reason: 'Cancelled by customer' }).then(r => r.data),
};
