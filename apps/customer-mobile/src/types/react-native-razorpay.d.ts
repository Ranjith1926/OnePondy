// react-native-razorpay ships JavaScript without type declarations, so the small part this app uses is declared here.
// Only src/features/payments/checkout/razorpayCheckout.ts imports it.
declare module 'react-native-razorpay' {
  export interface RazorpayOptions {
    key: string;
    order_id: string;
    amount: number;
    currency: string;
    name: string;
    description: string;
    theme?: { color?: string };
    /** Seconds the checkout stays open. */
    timeout?: number;
    retry?: { enabled: boolean; max_count?: number };
  }

  export interface RazorpaySuccess {
    razorpay_payment_id: string;
    razorpay_order_id?: string;
    razorpay_signature?: string;
  }

  export interface RazorpayFailure {
    code: number;
    description?: string;
  }

  const RazorpayCheckout: {
    open(options: RazorpayOptions): Promise<RazorpaySuccess>;
  };

  export default RazorpayCheckout;
}
