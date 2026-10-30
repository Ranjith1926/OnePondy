import { useMutation, useQueryClient } from '@tanstack/react-query';

import { foodApi, type OrderDetail } from '../../food/api/foodApi';
import { ordersQueryKey, orderQueryKey } from '../../food/hooks/useFood';
import { openRazorpayCheckout } from '../checkout/razorpayCheckout';
import { payFor, type PayDependencies } from '../paymentFlow';

const realDependencies: PayDependencies<OrderDetail> = {
  start: foodApi.startPayment,
  checkout: openRazorpayCheckout,
  refresh: foodApi.refreshPayment,
  delay: (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
  now: () => Date.now(),
};

/**
 * Pays for a food order the restaurant has accepted. The outcome, in `data`, always comes from asking the server; the order on
 * screen is refreshed afterwards so it shows what really happened. `dependencies` is for tests.
 */
export function usePayOrder(
  orderId: string,
  description: string,
  dependencies: PayDependencies<OrderDetail> = realDependencies,
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => payFor<OrderDetail>(orderId, description, dependencies),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: orderQueryKey(orderId) });
      void queryClient.invalidateQueries({ queryKey: ordersQueryKey });
    },
  });
}
