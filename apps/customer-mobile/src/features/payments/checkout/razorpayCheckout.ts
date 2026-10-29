import { Platform } from 'react-native';
import RazorpayCheckout from 'react-native-razorpay';

export interface CheckoutRequest {
  keyId: string;
  orderId: string;
  amountPaise: number;
  currency: string;
  description: string;
  /** How long the checkout may stay open, in seconds: the time left to pay. */
  timeoutSeconds: number;
}

/**
 * How the checkout ended, as far as the phone can tell. `completed` means the checkout reported a payment, never that the
 * booking is paid: only the server confirms that (spec Phase 7: "a success reported by the app is never trusted").
 */
export type CheckoutResult = { kind: 'completed' } | { kind: 'cancelled' } | { kind: 'failed' };

export type CheckoutLauncher = (request: CheckoutRequest) => Promise<CheckoutResult>;

/**
 * Razorpay's Android SDK code for the customer closing the checkout without paying (`Checkout.PAYMENT_CANCELED`). Code 2 is
 * `NETWORK_ERROR`, so it is a failure, not a cancel. Seen on a real phone. The iOS code is not known here, so on iOS every
 * error is reported as a failure, which only changes the wording shown.
 */
const ANDROID_CANCELLED_CODE = 0;

const brandColour = '#0E6F6A';

/** Opens Razorpay's checkout (the native SDK) and reports how it ended. Nothing in the reply is logged or kept. */
export const openRazorpayCheckout: CheckoutLauncher = async (request) => {
  try {
    await RazorpayCheckout.open({
      key: request.keyId,
      order_id: request.orderId,
      amount: request.amountPaise,
      currency: request.currency,
      name: 'OnePondy',
      description: request.description,
      theme: { color: brandColour },
      timeout: Math.max(60, request.timeoutSeconds),
      retry: { enabled: true, max_count: 3 },
    });
    return { kind: 'completed' };
  } catch (error) {
    const code = (error as { code?: unknown } | null)?.code;
    return Platform.OS === 'android' && code === ANDROID_CANCELLED_CODE
      ? { kind: 'cancelled' }
      : { kind: 'failed' };
  }
};
