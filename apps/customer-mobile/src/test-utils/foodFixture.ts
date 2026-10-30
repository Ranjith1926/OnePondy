import type {
  MenuItem,
  OrderDetail,
  OrderLine,
  OrderPrice,
  OrderQuote,
  PublicMenu,
  PublicRestaurant,
} from '../features/food/api/foodApi';
import { emptyCart, type CartState } from '../features/food/cart/cart';

export const restaurantId = '7c1d2a10-5f6e-4a77-9a3b-1b6f0e2d4c01';
export const dosaId = 'a0000000-0000-4000-8000-000000000001';
export const coffeeId = 'a0000000-0000-4000-8000-000000000002';
export const vadaId = 'a0000000-0000-4000-8000-000000000003';
export const orderId = '5e0c2f64-7a5b-4a52-9d58-0d1d4f2d9b22';

export function aRestaurant(overrides: Partial<PublicRestaurant> = {}): PublicRestaurant {
  return {
    businessId: restaurantId,
    name: 'Spice Route',
    description: 'Home-style South Indian food.',
    fssaiLicenseNumber: '12345678901234',
    prepTimeMinutes: 25,
    minOrderAmount: 100,
    deliveryAreas: ['whiteTown', 'tamilQuarter'],
    ...overrides,
  };
}

export function aDish(overrides: Partial<MenuItem> = {}): MenuItem {
  return {
    id: dosaId,
    categoryId: 'category-1',
    name: 'Masala dosa',
    description: 'Crisp, with potato masala.',
    price: 120,
    currency: 'INR',
    isVeg: true,
    taxRatePercent: 5,
    isAvailable: true,
    isArchived: false,
    position: 0,
    version: 1,
    ...overrides,
  };
}

/** A menu with a dosa (₹120), a filter coffee (₹45) and a sold-out vada. */
export function aMenu(overrides: Partial<PublicMenu> = {}): PublicMenu {
  return {
    restaurant: aRestaurant(),
    categories: [
      {
        id: 'category-1',
        name: 'Breakfast',
        position: 0,
        version: 1,
        items: [
          aDish(),
          aDish({ id: coffeeId, name: 'Filter coffee', description: null, price: 45, position: 1 }),
          aDish({ id: vadaId, name: 'Medu vada', price: 60, isAvailable: false, position: 2 }),
        ],
      },
    ],
    ...overrides,
  };
}

/** The cart a customer has after adding two dosas and a coffee. */
export function aCart(): CartState {
  return {
    restaurant: {
      businessId: restaurantId,
      name: 'Spice Route',
      minOrderAmount: 100,
      prepTimeMinutes: 25,
      deliveryAreas: ['whiteTown', 'tamilQuarter'],
    },
    lines: [
      { menuItemId: dosaId, name: 'Masala dosa', isVeg: true, unitPrice: 120, quantity: 2 },
      { menuItemId: coffeeId, name: 'Filter coffee', isVeg: true, unitPrice: 45, quantity: 1 },
    ],
  };
}

export { emptyCart };

export const aPrice: OrderPrice = {
  subtotal: 285,
  deliveryFee: 30,
  packagingFee: 10,
  taxAmount: 14.25,
  discountAmount: 0,
  totalAmount: 339.25,
  currency: 'INR',
};

export const orderLines: OrderLine[] = [
  {
    menuItemId: dosaId,
    name: 'Masala dosa',
    isVeg: true,
    unitPrice: 120,
    quantity: 2,
    taxRatePercent: 5,
    subtotal: 240,
    taxAmount: 12,
  },
  {
    menuItemId: coffeeId,
    name: 'Filter coffee',
    isVeg: true,
    unitPrice: 45,
    quantity: 1,
    taxRatePercent: 5,
    subtotal: 45,
    taxAmount: 2.25,
  },
];

export function aQuote(overrides: Partial<OrderQuote> = {}): OrderQuote {
  return {
    businessId: restaurantId,
    restaurantName: 'Spice Route',
    area: 'whiteTown',
    prepTimeMinutes: 25,
    minOrderAmount: 100,
    lines: orderLines,
    price: aPrice,
    ...overrides,
  };
}

/** An order the restaurant has accepted and that now waits for payment; tests override what they care about. */
export function anOrder(overrides: Partial<OrderDetail> = {}): OrderDetail {
  return {
    id: orderId,
    version: 2,
    businessId: restaurantId,
    restaurantName: 'Spice Route',
    fssaiLicenseNumber: '12345678901234',
    status: 'awaitingPayment',
    canCancel: true,
    expiresAt: '2026-10-30T10:30:00Z',
    estimatedReadyAt: null,
    prepTimeMinutes: 25,
    deliveryArea: 'whiteTown',
    deliveryAddress: '12 Rue Suffren, White Town',
    deliveryNote: null,
    contactPhone: '+919876500001',
    lines: orderLines,
    price: aPrice,
    closedReason: null,
    history: [],
    createdAt: '2026-10-30T10:00:00Z',
    updatedAt: '2026-10-30T10:05:00Z',
    payment: null,
    refunds: [],
    ...overrides,
  };
}
