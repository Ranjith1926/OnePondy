import { apiRequest } from '../../../api/apiClient';
import type { PaymentReceipt, RefundInfo } from '../../bookings/api/bookingsApi';

// Shapes returned by /api/v1/restaurants and /api/v1/food (spec Phase 8). Enums arrive in camelCase.

export interface PublicRestaurant {
  businessId: string;
  name: string;
  description: string | null;
  /** Shown for every restaurant (spec §21). */
  fssaiLicenseNumber: string | null;
  prepTimeMinutes: number;
  minOrderAmount: number;
  /** Area codes the restaurant delivers to; `puducherry` means the whole city. */
  deliveryAreas: string[];
}

export interface MenuItem {
  id: string;
  categoryId: string;
  name: string;
  description: string | null;
  price: number;
  currency: string;
  isVeg: boolean;
  taxRatePercent: number;
  /** False while the dish is sold out. */
  isAvailable: boolean;
  isArchived: boolean;
  position: number;
  version: number;
}

export interface MenuCategory {
  id: string;
  name: string;
  position: number;
  version: number;
  items: MenuItem[];
}

export interface PublicMenu {
  restaurant: PublicRestaurant;
  categories: MenuCategory[];
}

export type FoodOrderStatus =
  | 'placed'
  | 'awaitingPayment'
  | 'confirmed'
  | 'preparing'
  | 'readyForPickup'
  | 'outForDelivery'
  | 'delivered'
  | 'rejected'
  | 'cancelled'
  | 'expired'
  | 'failed'
  | 'refundPending'
  | 'refunded';

export type FoodOrderActorType = 'customer' | 'partner' | 'admin' | 'system' | 'driver';

/** One dish of a cart. The app never sends a price: the server reads it from the menu. */
export interface OrderLineInput {
  menuItemId: string;
  quantity: number;
}

export interface OrderLine {
  menuItemId: string;
  name: string;
  isVeg: boolean;
  unitPrice: number;
  quantity: number;
  taxRatePercent: number;
  subtotal: number;
  taxAmount: number;
}

export interface OrderPrice {
  subtotal: number;
  deliveryFee: number;
  packagingFee: number;
  taxAmount: number;
  discountAmount: number;
  totalAmount: number;
  currency: string;
}

/** The server's price for a cart, with the whole breakdown, before the customer commits. */
export type PaymentMethod = 'online' | 'cashOnDelivery';

/** How this cart can be paid for right now. Both can be true; if neither is, the cart cannot be ordered. */
export interface PaymentOptions {
  online: boolean;
  cashOnDelivery: boolean;
  /** The largest order that can be paid in cash. */
  cashOnDeliveryMaxAmount: number;
}

export interface OrderQuote {
  businessId: string;
  restaurantName: string;
  area: string;
  prepTimeMinutes: number;
  minOrderAmount: number;
  lines: OrderLine[];
  price: OrderPrice;
  paymentOptions: PaymentOptions;
}

export interface OrderHistoryEntry {
  previousStatus: FoodOrderStatus | null;
  status: FoodOrderStatus;
  actorType: FoodOrderActorType;
  reason: string | null;
  at: string;
}

export type DeliveryStatus = 'open' | 'accepted' | 'pickedUp' | 'delivered';

/** How the delivery is going. It names the driver by first name and vehicle only, and carries no position. */
export interface DeliveryProgress {
  status: DeliveryStatus;
  driverFirstName: string | null;
  vehicleType: 'bicycle' | 'scooter' | 'motorbike' | null;
  locationUpdatedAt: string | null;
}

export interface OrderDetail {
  id: string;
  version: number;
  businessId: string;
  restaurantName: string;
  fssaiLicenseNumber: string | null;
  status: FoodOrderStatus;
  /** True until the restaurant starts cooking (decision F8). */
  canCancel: boolean;
  expiresAt: string | null;
  estimatedReadyAt: string | null;
  prepTimeMinutes: number;
  deliveryArea: string;
  deliveryAddress: string;
  deliveryNote: string | null;
  contactPhone: string;
  lines: OrderLine[];
  price: OrderPrice;
  closedReason: string | null;
  history: OrderHistoryEntry[];
  createdAt: string;
  updatedAt: string;
  payment: PaymentReceipt | null;
  refunds: RefundInfo[];
  /** Null until the order is ready for a driver. */
  delivery: DeliveryProgress | null;
  paymentMethod: PaymentMethod;
  /** For cash on delivery: the cash the driver took at the door, once delivered. */
  cashCollected: number | null;
}

export interface OrderSummary {
  id: string;
  businessId: string;
  restaurantName: string;
  status: FoodOrderStatus;
  expiresAt: string | null;
  itemCount: number;
  totalAmount: number;
  currency: string;
  createdAt: string;
  paymentMethod: PaymentMethod;
}

export interface QuoteInput {
  businessId: string;
  area: string;
  lines: OrderLineInput[];
}

export interface PlaceOrderInput extends QuoteInput {
  /** A fresh key per attempt, so a retry never creates a second order. */
  idempotencyKey: string;
  address: string;
  note?: string;
  contactPhone: string;
  /** The total the customer was shown; the server refuses if the menu changed since. */
  expectedTotal: number;
  /** Left out, it is an online order. Cash on delivery is only accepted where the quote said it is offered. */
  paymentMethod?: PaymentMethod;
}

export const foodApi = {
  restaurants: (area: string) =>
    apiRequest<PublicRestaurant[]>(`/api/v1/restaurants?area=${encodeURIComponent(area)}`),

  menu: (businessId: string) =>
    apiRequest<PublicMenu>(`/api/v1/restaurants/${encodeURIComponent(businessId)}/menu`),

  quote: (input: QuoteInput) =>
    apiRequest<OrderQuote>('/api/v1/food/quote', { method: 'POST', body: input, auth: true }),

  place: (input: PlaceOrderInput) =>
    apiRequest<OrderDetail>('/api/v1/food/orders', { method: 'POST', body: input, auth: true }),

  orders: () => apiRequest<OrderSummary[]>('/api/v1/food/orders', { auth: true }),

  order: (id: string) =>
    apiRequest<OrderDetail>(`/api/v1/food/orders/${encodeURIComponent(id)}`, { auth: true }),

  cancel: (id: string, version: number, reason?: string) =>
    apiRequest<OrderDetail>(`/api/v1/food/orders/${encodeURIComponent(id)}/cancel`, {
      method: 'POST',
      body: { version, reason },
      auth: true,
    }),

  /** Creates (or returns the open) gateway order for an order the restaurant has accepted. Safe to repeat. */
  startPayment: (id: string) =>
    apiRequest<FoodPaymentStart>(`/api/v1/food/orders/${encodeURIComponent(id)}/payment`, {
      method: 'POST',
      auth: true,
    }),

  /** "Is it paid yet?" The server asks the gateway itself and returns the order as it now stands. */
  refreshPayment: (id: string) =>
    apiRequest<OrderDetail>(`/api/v1/food/orders/${encodeURIComponent(id)}/payment/refresh`, {
      method: 'POST',
      auth: true,
    }),
};

/** What the app needs to open the gateway's checkout for an order. The key id is public; the secret never leaves the server. */
export interface FoodPaymentStart {
  foodOrderId: string;
  attemptId: string;
  provider: 'razorpay';
  /** `sandbox` means test money: nothing real is charged. */
  mode: 'sandbox' | 'live';
  keyId: string;
  orderId: string;
  amount: number;
  amountPaise: number;
  currency: string;
  paymentExpiresAt: string;
}
