import { describe, expect, it } from 'vitest';

import type {
  DeliveryJobStatus,
  FoodOrderStatus,
  OrderActor,
  OrderAttention,
  PaymentMethod,
} from './apiTypes';
import {
  adminOrderStatusLabels,
  attentionLabels,
  deliveryStatusLabels,
  foodOrderStatusLabels,
  moduleLabels,
  orderActorLabels,
  paymentMethodLabels,
} from './labels';

// The API's enums are listed here by hand: a value the console has no label for would show as blank.
describe('administrators’ order labels', () => {
  it('has a label for every order status', () => {
    const statuses = Object.keys(foodOrderStatusLabels) as FoodOrderStatus[];
    expect(Object.keys(adminOrderStatusLabels).sort()).toEqual([...statuses].sort());
    for (const status of statuses) {
      expect(adminOrderStatusLabels[status].trim()).not.toBe('');
    }
  });

  it('speaks of the restaurant, not of "you", for a new order', () => {
    expect(adminOrderStatusLabels.placed).toBe('Waiting for the restaurant');
    expect(foodOrderStatusLabels.placed).toMatch(/waiting for you/i);
  });

  it('has a label for every reason an order can need attention', () => {
    const reasons: OrderAttention[] = [
      'noDriver',
      'pickupOverdue',
      'deliveryOverdue',
      'kitchenOverdue',
      'refundOverdue',
    ];
    expect(Object.keys(attentionLabels).sort()).toEqual([...reasons].sort());
  });

  it('has a label for every delivery state and every actor', () => {
    const states: DeliveryJobStatus[] = ['open', 'accepted', 'pickedUp', 'delivered', 'cancelled'];
    const actors: OrderActor[] = ['customer', 'partner', 'admin', 'system', 'driver'];
    expect(Object.keys(deliveryStatusLabels).sort()).toEqual([...states].sort());
    expect(Object.keys(orderActorLabels).sort()).toEqual([...actors].sort());
  });

  it('has a label for both ways of paying', () => {
    const methods: PaymentMethod[] = ['online', 'cashOnDelivery'];
    expect(Object.keys(paymentMethodLabels).sort()).toEqual([...methods].sort());
  });

  it('names the cash on delivery switch and online payment in the module list', () => {
    expect(moduleLabels.cashOnDelivery).toBe('Cash on delivery');
    expect(moduleLabels.payments).toBe('Online payment');
  });
});
