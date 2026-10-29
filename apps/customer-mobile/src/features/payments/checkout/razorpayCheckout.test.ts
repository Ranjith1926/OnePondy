import RazorpayCheckout from 'react-native-razorpay';

import { openRazorpayCheckout } from './razorpayCheckout';

const open = RazorpayCheckout.open as jest.Mock;

const request = {
  keyId: 'rzp_test_key',
  orderId: 'order_1',
  amountPaise: 100000,
  currency: 'INR',
  description: 'Sunrise boat ride',
  timeoutSeconds: 1200,
};

describe('openRazorpayCheckout', () => {
  beforeEach(() => open.mockReset());

  it('opens the checkout with the server-made order, never a secret', async () => {
    open.mockResolvedValue({ razorpay_payment_id: 'pay_1' });

    expect(await openRazorpayCheckout(request)).toEqual({ kind: 'completed' });

    const options = open.mock.calls[0][0];
    expect(options).toMatchObject({
      key: 'rzp_test_key',
      order_id: 'order_1',
      amount: 100000,
      currency: 'INR',
      timeout: 1200,
    });
    expect(JSON.stringify(options)).not.toMatch(/secret/i);
  });

  it('never asks for a timeout under a minute', async () => {
    open.mockResolvedValue({});

    await openRazorpayCheckout({ ...request, timeoutSeconds: 20 });

    expect(open.mock.calls[0][0].timeout).toBe(60);
  });

  it('reports a closed checkout as cancelled', async () => {
    open.mockRejectedValue({ code: 2, description: 'Payment Cancelled' });

    expect(await openRazorpayCheckout(request)).toEqual({ kind: 'cancelled' });
  });

  it.each([{ code: 0 }, { code: 1 }, new Error('boom'), null, undefined])(
    'reports any other failure (%p) as failed',
    async (error) => {
      open.mockRejectedValue(error);

      expect(await openRazorpayCheckout(request)).toEqual({ kind: 'failed' });
    },
  );
});
