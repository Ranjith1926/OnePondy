import { describe, expect, it } from 'vitest';

import type { FoodOrderStatus, PartnerOrderSummary } from './apiTypes';
import {
  deadlineFor,
  isOpen,
  isOrderStep,
  openStatuses,
  ordersForView,
  parseView,
  stepNeedsReason,
  stepsFor,
  toneFor,
  waitingForYou,
} from './foodOrders';
import { foodOrderStatusLabels } from './labels';

const allStatuses = Object.keys(foodOrderStatusLabels) as FoodOrderStatus[];

function anOrder(overrides: Partial<PartnerOrderSummary> = {}): PartnerOrderSummary {
  return {
    id: 'o1',
    version: 4,
    status: 'placed',
    expiresAt: '2026-10-30T05:05:00Z',
    estimatedReadyAt: null,
    itemCount: 3,
    lines: [],
    totalAmount: 339.25,
    currency: 'INR',
    deliveryArea: 'whiteTown',
    createdAt: '2026-10-30T05:00:00Z',
    ...overrides,
  };
}

const stepNames = (status: FoodOrderStatus) => stepsFor(status).map((option) => option.step);

describe('what the restaurant can do with an order', () => {
  it('accepts or rejects a new order', () => {
    expect(stepNames('placed')).toEqual(['accept', 'reject']);
    expect(stepsFor('placed')[0]).toMatchObject({ step: 'accept', primary: true });
  });

  it('can only withdraw an order the customer has yet to pay for', () => {
    expect(stepNames('awaitingPayment')).toEqual(['reject']);
  });

  it('moves a paid order through the kitchen, and may cancel it until it leaves', () => {
    expect(stepNames('confirmed')).toEqual(['preparing', 'cancel']);
    expect(stepNames('preparing')).toEqual(['ready', 'cancel']);
    expect(stepNames('readyForPickup')).toEqual(['cancel']);
  });

  it('offers nothing once the driver has it or the order is closed', () => {
    for (const status of [
      'outForDelivery',
      'delivered',
      'rejected',
      'cancelled',
      'expired',
      'failed',
      'refundPending',
      'refunded',
    ] as const) {
      expect(stepNames(status)).toEqual([]);
    }
  });

  it('never offers an action the API has no endpoint for', () => {
    for (const status of allStatuses) {
      for (const { step } of stepsFor(status)) {
        expect(isOrderStep(step)).toBe(true);
      }
    }
  });

  it('asks for a reason only when the customer has to be told one', () => {
    expect(stepNeedsReason('reject')).toBe(true);
    expect(stepNeedsReason('cancel')).toBe(true);
    for (const step of ['accept', 'preparing', 'ready'] as const) {
      expect(stepNeedsReason(step)).toBe(false);
    }
  });

  it('recognises only its own actions', () => {
    expect(isOrderStep('accept')).toBe(true);
    expect(isOrderStep('deliver')).toBe(false);
    expect(isOrderStep(undefined)).toBe(false);
    expect(isOrderStep(null)).toBe(false);
  });
});

describe('the inbox views', () => {
  const orders = [
    anOrder({ id: 'newer-open', status: 'confirmed', createdAt: '2026-10-30T06:00:00Z' }),
    anOrder({ id: 'done', status: 'delivered', createdAt: '2026-10-30T07:00:00Z' }),
    anOrder({ id: 'older-open', status: 'placed', createdAt: '2026-10-30T05:00:00Z' }),
    anOrder({ id: 'gone', status: 'cancelled', createdAt: '2026-10-30T04:00:00Z' }),
  ];

  it('shows open orders by default, the ones waiting for the restaurant first', () => {
    expect(ordersForView(orders, 'open').map((o) => o.id)).toEqual(['older-open', 'newer-open']);
  });

  it('puts a new order ahead of older ones that need nothing from the restaurant, then the longest-waiting first', () => {
    const mixed = [
      anOrder({ id: 'cooking-old', status: 'preparing', createdAt: '2026-10-30T04:00:00Z' }),
      anOrder({ id: 'paying', status: 'awaitingPayment', createdAt: '2026-10-30T04:10:00Z' }),
      anOrder({ id: 'new-b', status: 'placed', createdAt: '2026-10-30T05:20:00Z' }),
      anOrder({ id: 'new-a', status: 'placed', createdAt: '2026-10-30T05:10:00Z' }),
      anOrder({ id: 'paid', status: 'confirmed', createdAt: '2026-10-30T04:30:00Z' }),
      anOrder({ id: 'ready', status: 'readyForPickup', createdAt: '2026-10-30T03:00:00Z' }),
    ];

    expect(ordersForView(mixed, 'open').map((o) => o.id)).toEqual([
      'new-a',
      'new-b',
      'paid',
      'cooking-old',
      'ready',
      'paying',
    ]);
  });

  it('shows everything in the order the API gave it', () => {
    expect(ordersForView(orders, 'all').map((o) => o.id)).toEqual(orders.map((o) => o.id));
  });

  it('shows one status on its own', () => {
    expect(ordersForView(orders, 'cancelled').map((o) => o.id)).toEqual(['gone']);
  });

  it('does not reorder the list it was given', () => {
    const before = orders.map((o) => o.id);
    ordersForView(orders, 'open');
    expect(orders.map((o) => o.id)).toEqual(before);
  });

  it('counts the orders waiting for the restaurant', () => {
    expect(waitingForYou(orders)).toBe(1);
    expect(waitingForYou([])).toBe(0);
  });

  it('falls back to the open orders for a view it does not know', () => {
    expect(parseView(undefined, allStatuses)).toBe('open');
    expect(parseView('nonsense', allStatuses)).toBe('open');
    expect(parseView('all', allStatuses)).toBe('all');
    expect(parseView('preparing', allStatuses)).toBe('preparing');
  });
});

describe('what an order looks like', () => {
  it('treats the orders that still need someone as open', () => {
    expect(isOpen('placed')).toBe(true);
    expect(isOpen('readyForPickup')).toBe(true);
    expect(isOpen('delivered')).toBe(false);
    expect(openStatuses.every((status) => allStatuses.includes(status))).toBe(true);
  });

  it('labels every status in plain words and tones it', () => {
    for (const status of allStatuses) {
      expect(foodOrderStatusLabels[status].trim()).not.toBe('');
      expect(toneFor(status)).toBeTruthy();
    }
    expect(foodOrderStatusLabels.placed).toMatch(/waiting for you/i);
  });

  it('stands out when an order is waiting for the restaurant and when it was rejected', () => {
    expect(toneFor('placed')).toBe('warn');
    expect(toneFor('rejected')).toBe('danger');
    expect(toneFor('delivered')).toBe('ok');
  });

  it('shows the one deadline that matters for each stage', () => {
    expect(deadlineFor(anOrder({ status: 'placed' }))).toEqual({
      label: 'Respond by',
      at: '2026-10-30T05:05:00Z',
    });
    expect(deadlineFor(anOrder({ status: 'awaitingPayment' }))?.label).toBe('Customer pays by');
    expect(
      deadlineFor(
        anOrder({ status: 'preparing', expiresAt: null, estimatedReadyAt: '2026-10-30T05:30:00Z' }),
      ),
    ).toEqual({ label: 'Ready by', at: '2026-10-30T05:30:00Z' });
    expect(deadlineFor(anOrder({ status: 'confirmed', expiresAt: null }))).toBeNull();
    expect(deadlineFor(anOrder({ status: 'delivered', expiresAt: null }))).toBeNull();
  });
});
