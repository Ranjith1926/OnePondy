import { ApiError, NetworkError } from '../../api/apiErrors';
import { serviceAreaIds } from '../location/serviceAreas';
import { servedAreas, startingArea } from './deliveryAreas';
import { fieldError, foodErrorMessageKey } from './foodErrors';
import { orderRefetchInterval } from './hooks/useFood';

describe('servedAreas', () => {
  it('lists every specific area for a restaurant that delivers across the whole city', () => {
    const served = servedAreas(['puducherry']);

    expect(served).toEqual(serviceAreaIds.filter((id) => id !== 'puducherry'));
    expect(served).not.toContain('puducherry');
  });

  it('lists just the areas a restaurant names, never the whole-city code', () => {
    expect(servedAreas(['whiteTown', 'auroville'])).toEqual(['whiteTown', 'auroville']);
    expect(servedAreas([])).toEqual([]);
  });
});

describe('startingArea', () => {
  const served = servedAreas(['whiteTown', 'auroville']);

  it('keeps the area the customer chose when the restaurant delivers there', () => {
    expect(startingArea('auroville', served)).toBe('auroville');
  });

  it('falls back to the restaurant’s first area for the whole city, another area or none', () => {
    expect(startingArea('puducherry', served)).toBe('whiteTown');
    expect(startingArea('lawspet', served)).toBe('whiteTown');
    expect(startingArea(null, served)).toBe('whiteTown');
    expect(startingArea('whiteTown', [])).toBeNull();
  });
});

describe('foodErrorMessageKey', () => {
  it('names what happened for every error the server can give', () => {
    expect(foodErrorMessageKey(new ApiError(409, 'order_price_changed'))).toBe(
      'food.errors.order_price_changed',
    );
    expect(foodErrorMessageKey(new ApiError(409, 'refunds_not_enabled'))).toBe(
      'food.errors.refunds_not_enabled',
    );
    expect(foodErrorMessageKey(new ApiError(409, 'restaurant_not_taking_orders'))).toBe(
      'food.errors.restaurant_not_taking_orders',
    );
  });

  it('tells a lost connection from a slow one, and anything else is generic', () => {
    expect(foodErrorMessageKey(new NetworkError('offline'))).toBe('food.errors.offline');
    expect(foodErrorMessageKey(new NetworkError('timeout'))).toBe('food.errors.timeout');
    expect(foodErrorMessageKey(new ApiError(500, 'http_500'))).toBe('food.errors.generic');
    expect(foodErrorMessageKey(new Error('x'))).toBe('food.errors.generic');
  });
});

describe('fieldError', () => {
  it('reads the first message for a field', () => {
    const error = new ApiError(400, 'validation_failed', undefined, undefined, {
      address: ['Required.'],
    });

    expect(fieldError(error, 'address')).toBe('Required.');
    expect(fieldError(error, 'contactPhone')).toBeUndefined();
    expect(fieldError(new Error('x'), 'address')).toBeUndefined();
  });
});

describe('orderRefetchInterval', () => {
  it('re-reads quickly while the restaurant or the payment is awaited', () => {
    expect(orderRefetchInterval('placed')).toBe(5_000);
    expect(orderRefetchInterval('awaitingPayment')).toBe(5_000);
  });

  it('re-reads slowly while the kitchen, the driver or a refund is in progress', () => {
    for (const status of [
      'confirmed',
      'preparing',
      'readyForPickup',
      'outForDelivery',
      'refundPending',
    ] as const) {
      expect(orderRefetchInterval(status)).toBe(15_000);
    }
  });

  it('stops once the order is finished or closed', () => {
    for (const status of [
      'delivered',
      'rejected',
      'cancelled',
      'expired',
      'failed',
      'refunded',
      undefined,
    ] as const) {
      expect(orderRefetchInterval(status)).toBe(false);
    }
  });
});
