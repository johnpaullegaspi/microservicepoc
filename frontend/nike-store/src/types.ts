export interface Product { id: string; productName: string; description: string; sku: string; price: number; stockQuantity: number; imageUrl: string; }
export interface OrderItem { productId: string; productName: string; quantity: number; unitPrice: number; subtotal: number; }
export interface Order {
  id: string; orderNumber: string; customerName: string; customerEmail: string; contactNumber: string; deliveryAddress: string;
  paymentMethod: string; totalAmount: number; orderStatus: string; paymentStatus: string;
  transactionReference?: string | null; failureReason?: string | null; createdAt: string; updatedAt: string; items: OrderItem[];
}
export interface CartLine { product: Product; quantity: number; }
export interface CheckoutForm { customerName: string; customerEmail: string; contactNumber: string; deliveryAddress: string; paymentMethod: string; }
