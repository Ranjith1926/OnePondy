import { ApiError } from '../../api/apiErrors';
import { aBooking, bookingId } from '../../test-utils/bookingFixture';
import type { PaymentStart } from './api/paymentsApi';
import type { CheckoutResult } from './checkout/razorpayCheckout';
import { payForBooking, type PayDependencies } from './paymentFlow';

const now = Date.parse('2026-10-29T10:00:00Z');

const started: PaymentStart = {
  bookingId,
  attemptId: 'attempt-1',
  provider: 'razorpay',
  mode: 'sandbox',
  keyId: 'rzp_test_key',
  orderId: 'order_1',
  amount: 1000,
  amountPaise: 100000,
  currency: 'INR',
  paymentExpiresAt: '2026-10-29T10:30:00Z',
};

function setUp(checkout: CheckoutResult, refreshes: (() => Promise<unknown>)[]) {
  const refresh = jest.fn();
  refreshes.forEach((reply) => refresh.mockImplementationOnce(reply));
  const deps: PayDependencies = {
    start: jest.fn().mockResolvedValue(started),
    checkout: jest.fn().mockResolvedValue(checkout),
    refresh,
    delay: jest.fn().mockResolvedValue(undefined),
    now: () => now,
  };
  return deps;
}

const reply =
  (overrides: Parameters<typeof aBooking>[0] = {}) =>
  () =>
    Promise.resolve(aBooking(overrides));

describe('payForBooking', () => {
  it('opens the checkout with the order the server made and the time left', async () => {
    const deps = setUp({ kind: 'completed' }, [reply({ status: 'confirmed' })]);

    await payForBooking(bookingId, 'Sunrise boat ride', deps);

    expect(deps.checkout).toHaveBeenCalledWith({
      keyId: 'rzp_test_key',
      orderId: 'order_1',
      amountPaise: 100000,
      currency: 'INR',
      description: 'Sunrise boat ride',
      timeoutSeconds: 1800,
    });
  });

  it('is confirmed only when the server says so', async () => {
    const deps = setUp({ kind: 'completed' }, [reply({ status: 'confirmed' })]);

    const outcome = await payForBooking(bookingId, 'x', deps);

    expect(outcome.kind).toBe('confirmed');
    expect(deps.refresh).toHaveBeenCalledTimes(1);
  });

  it('keeps asking after a reported payment until the server has seen it', async () => {
    const deps = setUp({ kind: 'completed' }, [
      reply({ status: 'awaitingPayment' }),
      reply({ status: 'awaitingPayment' }),
      reply({ status: 'confirmed' }),
    ]);

    const outcome = await payForBooking(bookingId, 'x', deps, { pollIntervalMs: 10 });

    expect(outcome.kind).toBe('confirmed');
    expect(deps.refresh).toHaveBeenCalledTimes(3);
    expect(deps.delay).toHaveBeenCalledWith(10);
  });

  it('says processing when the server has not seen the payment after the last ask', async () => {
    const deps = setUp({ kind: 'completed' }, []);
    (deps.refresh as jest.Mock).mockImplementation(reply({ status: 'awaitingPayment' }));

    const outcome = await payForBooking(bookingId, 'x', deps, { maxPolls: 3 });

    expect(outcome.kind).toBe('processing');
    // One ask right after the checkout, then three more.
    expect(deps.refresh).toHaveBeenCalledTimes(4);
  });

  it('says processing, not failed, when the app is offline after a reported payment', async () => {
    const deps = setUp({ kind: 'completed' }, []);
    (deps.refresh as jest.Mock).mockRejectedValue(new Error('offline'));

    const outcome = await payForBooking(bookingId, 'x', deps, { maxPolls: 2 });

    expect(outcome).toEqual({ kind: 'processing', booking: null });
  });

  it('reports a payment that arrived after the booking lapsed as lapsed', async () => {
    const deps = setUp({ kind: 'completed' }, [reply({ status: 'refundPending' })]);

    const outcome = await payForBooking(bookingId, 'x', deps);

    expect(outcome.kind).toBe('lapsed');
    expect(deps.delay).not.toHaveBeenCalled();
  });

  it.each(['cancelled', 'failed'] as const)(
    'passes a %s checkout through without polling',
    async (kind) => {
      const deps = setUp({ kind }, [reply({ status: 'awaitingPayment' })]);

      const outcome = await payForBooking(bookingId, 'x', deps);

      expect(outcome.kind).toBe(kind);
      expect(deps.refresh).toHaveBeenCalledTimes(1);
    },
  );

  it('still trusts the server over a cancelled checkout that actually paid', async () => {
    const deps = setUp({ kind: 'cancelled' }, [reply({ status: 'confirmed' })]);

    expect((await payForBooking(bookingId, 'x', deps)).kind).toBe('confirmed');
  });

  it('does not open the checkout when the time to pay has run out', async () => {
    const deps = setUp({ kind: 'completed' }, []);
    (deps.start as jest.Mock).mockResolvedValue({
      ...started,
      paymentExpiresAt: '2026-10-29T09:59:00Z',
    });

    const outcome = await payForBooking(bookingId, 'x', deps);

    expect(deps.checkout).not.toHaveBeenCalled();
    expect(outcome).toEqual({
      kind: 'error',
      error: expect.objectContaining({ code: 'payment_window_closed' }),
    });
  });

  it('returns the error when the server refuses to start a payment', async () => {
    const deps = setUp({ kind: 'completed' }, []);
    const refusal = new ApiError(409, 'payment_not_due');
    (deps.start as jest.Mock).mockRejectedValue(refusal);

    expect(await payForBooking(bookingId, 'x', deps)).toEqual({ kind: 'error', error: refusal });
    expect(deps.checkout).not.toHaveBeenCalled();
  });
});
