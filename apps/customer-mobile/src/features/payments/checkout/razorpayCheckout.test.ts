import { Platform } from 'react-native';
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

  describe('on Android', () => {
    beforeEach(() => jest.replaceProperty(Platform, 'OS', 'android'));
    afterEach(() => jest.restoreAllMocks());

    it('reports a closed checkout (the SDK code PAYMENT_CANCELED, 0) as cancelled', async () => {
      open.mockRejectedValue({ code: 0, description: 'Payment Cancelled' });

      expect(await openRazorpayCheckout(request)).toEqual({ kind: 'cancelled' });
    });

    it('reports a network error (code 2) as failed, not cancelled', async () => {
      open.mockRejectedValue({ code: 2, description: 'Network error' });

      expect(await openRazorpayCheckout(request)).toEqual({ kind: 'failed' });
    });

    it.each([{ code: 3 }, { code: 6 }, new Error('boom'), null, undefined])(
      'reports any other failure (%p) as failed',
      async (error) => {
        open.mockRejectedValue(error);

        expect(await openRazorpayCheckout(request)).toEqual({ kind: 'failed' });
      },
    );
  });

  it('never reports a cancel on iOS, whose codes are not known here', async () => {
    jest.replaceProperty(Platform, 'OS', 'ios');
    open.mockRejectedValue({ code: 0 });

    expect(await openRazorpayCheckout(request)).toEqual({ kind: 'failed' });
    jest.restoreAllMocks();
  });
});
