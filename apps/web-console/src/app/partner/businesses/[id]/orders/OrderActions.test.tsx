import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { FoodOrderStatus, PartnerOrderSummary } from '@/lib/apiTypes';

import { OrderActions } from './OrderActions';

// The server action reaches the API and the session; these tests are about what the buttons offer.
vi.mock('./actions', () => ({ changeOrderStatus: vi.fn() }));

const businessId = 'b0000000-0000-4000-8000-000000000001';

function anOrder(status: FoodOrderStatus): PartnerOrderSummary {
  return {
    id: 'o0000000-0000-4000-8000-000000000001',
    version: 4242,
    status,
    expiresAt: null,
    estimatedReadyAt: null,
    itemCount: 3,
    lines: [],
    totalAmount: 339.25,
    currency: 'INR',
    deliveryArea: 'whiteTown',
    createdAt: '2026-10-30T05:00:00Z',
  };
}

const show = (status: FoodOrderStatus, view = 'open') =>
  render(<OrderActions businessId={businessId} order={anOrder(status)} view={view} />);

const hidden = (container: HTMLElement, name: string) =>
  container.querySelector<HTMLInputElement>(`input[type="hidden"][name="${name}"]`)?.value;

describe('<OrderActions />', () => {
  it('offers Accept and Reject for a new order', () => {
    show('placed');

    expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument();
    // "Reject" is both the heading that opens the reason form and its submit button.
    expect(screen.getAllByText('Reject')).toHaveLength(2);
  });

  it('asks for a reason before rejecting, and says the customer is told it', () => {
    show('placed');

    expect(screen.getByLabelText('Reason (the customer is told)')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument();
  });

  it('offers to start preparing, or cancel, once the order is confirmed', () => {
    show('confirmed');

    expect(screen.getByRole('button', { name: 'Start preparing' })).toBeInTheDocument();
    expect(screen.getAllByText('Cancel order').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Accept' })).not.toBeInTheDocument();
  });

  it('offers Mark ready while it is being prepared', () => {
    show('preparing');

    expect(screen.getByRole('button', { name: 'Mark ready' })).toBeInTheDocument();
  });

  it('offers only a cancel once the order is ready, because the driver takes it from there', () => {
    show('readyForPickup');

    expect(
      screen.queryByRole('button', { name: /Mark ready|Start preparing|Accept/ }),
    ).not.toBeInTheDocument();
    expect(screen.getAllByText('Cancel order').length).toBeGreaterThan(0);
  });

  it('lets the restaurant withdraw an order that waits for the customer to pay', () => {
    show('awaitingPayment');

    expect(screen.getAllByText('Withdraw order').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Accept' })).not.toBeInTheDocument();
  });

  it.each([
    'outForDelivery',
    'delivered',
    'rejected',
    'cancelled',
    'expired',
    'refundPending',
    'refunded',
  ] as const)('shows no buttons for an order that is %s', (status) => {
    const { container } = show(status);

    expect(container).toBeEmptyDOMElement();
  });

  it('sends back the order, the version it is looking at and the view to return to', () => {
    const { container } = show('placed', 'all');

    expect(hidden(container, 'businessId')).toBe(businessId);
    expect(hidden(container, 'id')).toBe('o0000000-0000-4000-8000-000000000001');
    expect(hidden(container, 'version')).toBe('4242');
    expect(hidden(container, 'view')).toBe('all');
    expect(
      [...container.querySelectorAll('input[name="step"]')].map(
        (i) => (i as HTMLInputElement).value,
      ),
    ).toEqual(['accept', 'reject']);
  });
});
