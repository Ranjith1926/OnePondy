import { isApiError, isNetworkError } from '../../api/apiErrors';

const knownCodes = [
  'order_changed',
  'order_invalid_transition',
  'order_not_found',
  'order_item_unavailable',
  'order_below_minimum',
  'order_price_changed',
  'food_not_available',
  'restaurant_not_found',
  'restaurant_not_taking_orders',
  'restaurant_area_not_served',
  'refunds_not_enabled',
  'payment_not_due',
  'payment_window_closed',
  'payments_unavailable',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type FoodErrorMessageKey = `food.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps an ordering or payment failure to a translatable message that says what happened and what to do. */
export function foodErrorMessageKey(error: unknown): FoodErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'food.errors.timeout' : 'food.errors.offline';
  }

  if (isApiError(error) && isKnownCode(error.code)) {
    return `food.errors.${error.code}`;
  }

  return 'food.errors.generic';
}

/** The first message the server gave for one field of a form (such as the address), if any. */
export function fieldError(error: unknown, field: string): string | undefined {
  return isApiError(error) ? error.fieldErrors?.[field]?.[0] : undefined;
}
