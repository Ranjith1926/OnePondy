import type { FoodOrderStatus, PartnerOrderSummary } from './apiTypes';

/** The restaurant's actions on an order. Each is one POST to `/orders/{id}/{step}` and carries the order's version. */
export type OrderStep = 'accept' | 'reject' | 'preparing' | 'ready' | 'cancel';

export const orderSteps: readonly OrderStep[] = [
  'accept',
  'reject',
  'preparing',
  'ready',
  'cancel',
];

export const isOrderStep = (value: unknown): value is OrderStep =>
  typeof value === 'string' && (orderSteps as readonly string[]).includes(value);

/** Rejecting and cancelling need a reason: the customer is told it (the API refuses without one). */
export const stepNeedsReason = (step: OrderStep): boolean => step === 'reject' || step === 'cancel';

export interface StepOption {
  step: OrderStep;
  label: string;
  pendingLabel: string;
  /** The main thing to do next, shown as a filled button; the rest are tucked away behind a heading. */
  primary: boolean;
}

/**
 * What the restaurant can do with an order in each status. This only decides what to offer: the API checks every move against
 * its own table, so an offer that is out of date is refused rather than applied.
 */
export function stepsFor(status: FoodOrderStatus): StepOption[] {
  switch (status) {
    case 'placed':
      return [
        { step: 'accept', label: 'Accept', pendingLabel: 'Accepting…', primary: true },
        { step: 'reject', label: 'Reject', pendingLabel: 'Rejecting…', primary: false },
      ];
    case 'awaitingPayment':
      return [
        { step: 'reject', label: 'Withdraw order', pendingLabel: 'Withdrawing…', primary: false },
      ];
    case 'confirmed':
      return [
        { step: 'preparing', label: 'Start preparing', pendingLabel: 'Saving…', primary: true },
        { step: 'cancel', label: 'Cancel order', pendingLabel: 'Cancelling…', primary: false },
      ];
    case 'preparing':
      return [
        { step: 'ready', label: 'Mark ready', pendingLabel: 'Saving…', primary: true },
        { step: 'cancel', label: 'Cancel order', pendingLabel: 'Cancelling…', primary: false },
      ];
    case 'readyForPickup':
      return [
        { step: 'cancel', label: 'Cancel order', pendingLabel: 'Cancelling…', primary: false },
      ];
    default:
      return [];
  }
}

/** Orders that still need someone: the restaurant, the customer's payment, the kitchen or a driver. */
export const openStatuses: readonly FoodOrderStatus[] = [
  'placed',
  'awaitingPayment',
  'confirmed',
  'preparing',
  'readyForPickup',
  'outForDelivery',
  'refundPending',
];

export const isOpen = (status: FoodOrderStatus): boolean => openStatuses.includes(status);

/** Which view of the inbox is asked for: the open orders (the default), everything, or one status. */
export type InboxView = 'open' | 'all' | FoodOrderStatus;

export function parseView(value: string | undefined, valid: readonly string[]): InboxView {
  return value === 'all' || (value !== undefined && valid.includes(value))
    ? (value as InboxView)
    : 'open';
}

/**
 * How soon the restaurant needs to look at an order in each status: a new order waits on them for a few minutes only, then come
 * the kitchen's stages in the order they happen, and last the ones that are with the customer, a driver or a refund.
 */
const urgency: Partial<Record<FoodOrderStatus, number>> = {
  placed: 0,
  confirmed: 1,
  preparing: 2,
  readyForPickup: 3,
  awaitingPayment: 4,
  outForDelivery: 5,
  refundPending: 6,
};

/**
 * The orders to show for a view. Open orders come in tiers: what needs the restaurant first (a new order, then the kitchen's
 * stages), and within each the one that has waited longest first. Any other view is newest first, as the API returns it.
 */
export function ordersForView(
  orders: readonly PartnerOrderSummary[],
  view: InboxView,
): PartnerOrderSummary[] {
  if (view === 'open') {
    return orders
      .filter((order) => isOpen(order.status))
      .toSorted(
        (a, b) =>
          (urgency[a.status] ?? 99) - (urgency[b.status] ?? 99) ||
          Date.parse(a.createdAt) - Date.parse(b.createdAt),
      );
  }

  return view === 'all' ? [...orders] : orders.filter((order) => order.status === view);
}

/** How many orders are waiting for the restaurant to accept, for the heading. */
export const waitingForYou = (orders: readonly PartnerOrderSummary[]): number =>
  orders.filter((order) => order.status === 'placed').length;

export type OrderTone = 'info' | 'ok' | 'warn' | 'danger' | 'muted';

export function toneFor(status: FoodOrderStatus): OrderTone {
  switch (status) {
    case 'placed':
      return 'warn';
    case 'awaitingPayment':
      return 'info';
    case 'confirmed':
    case 'preparing':
    case 'readyForPickup':
    case 'outForDelivery':
    case 'delivered':
      return 'ok';
    case 'rejected':
    case 'failed':
      return 'danger';
    case 'refundPending':
      return 'warn';
    default:
      return 'muted';
  }
}

/** The one deadline worth showing for an order, if it has one: when to answer, when the customer must pay by, or when it should be ready. */
export function deadlineFor(order: PartnerOrderSummary): { label: string; at: string } | null {
  if (order.status === 'placed' && order.expiresAt) {
    return { label: 'Respond by', at: order.expiresAt };
  }

  if (order.status === 'awaitingPayment' && order.expiresAt) {
    return { label: 'Customer pays by', at: order.expiresAt };
  }

  if (order.status === 'preparing' && order.estimatedReadyAt) {
    return { label: 'Ready by', at: order.estimatedReadyAt };
  }

  return null;
}
