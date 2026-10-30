import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useAppState } from '../../../state';
import {
  foodApi,
  type FoodOrderStatus,
  type OrderLineInput,
  type PlaceOrderInput,
} from '../api/foodApi';

export const restaurantsQueryKey = (area: string) => ['restaurants', area] as const;
export const menuQueryKey = (businessId: string) => ['restaurant-menu', businessId] as const;
export const ordersQueryKey = ['food-orders'] as const;
export const orderQueryKey = (id: string) => ['food-order', id] as const;

/** Open restaurants that deliver to an area: no sign-in needed to look around. */
export function useRestaurants(area: string) {
  return useQuery({
    queryKey: restaurantsQueryKey(area),
    queryFn: () => foodApi.restaurants(area),
  });
}

export function useRestaurantMenu(businessId: string) {
  return useQuery({
    queryKey: menuQueryKey(businessId),
    queryFn: () => foodApi.menu(businessId),
  });
}

/**
 * The server's price for the cart, with the whole breakdown. Needs an account, so it only asks while signed in. It is read fresh each
 * time the screen opens, because a price shown must be the price the order is placed at (the server checks this too).
 */
export function useQuote(
  businessId: string | undefined,
  area: string | null,
  lines: OrderLineInput[],
) {
  const { auth } = useAppState();
  return useQuery({
    queryKey: ['food-quote', businessId, area, lines] as const,
    queryFn: () => foodApi.quote({ businessId: businessId!, area: area!, lines }),
    enabled:
      auth.status === 'authenticated' &&
      businessId !== undefined &&
      area !== null &&
      lines.length > 0,
    staleTime: 0,
    gcTime: 0,
    retry: false,
  });
}

export function usePlaceOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: PlaceOrderInput) => foodApi.place(input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ordersQueryKey });
    },
  });
}

/** Only asks while signed in: a read made before signing in fails with "not signed in", and that failure would stay cached after. */
export function useOrders() {
  const { auth } = useAppState();
  return useQuery({
    queryKey: ordersQueryKey,
    queryFn: () => foodApi.orders(),
    enabled: auth.status === 'authenticated',
  });
}

/**
 * While an order is waiting on the restaurant, on payment, on the kitchen or on a refund, the screen re-reads it so it updates by
 * itself (the restaurant or the webhook may act at any moment). A finished order stops asking.
 */
export function orderRefetchInterval(status: FoodOrderStatus | undefined): number | false {
  switch (status) {
    case 'placed':
    case 'awaitingPayment':
      return 5_000;
    case 'confirmed':
    case 'preparing':
    case 'readyForPickup':
    case 'outForDelivery':
    case 'refundPending':
      return 15_000;
    default:
      return false;
  }
}

export function useOrder(id: string | undefined) {
  const { auth } = useAppState();
  return useQuery({
    queryKey: orderQueryKey(id ?? ''),
    queryFn: () => foodApi.order(id!),
    enabled: id !== undefined && auth.status === 'authenticated',
    refetchInterval: (query) => orderRefetchInterval(query.state.data?.status),
  });
}

export function useCancelOrder(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ version, reason }: { version: number; reason?: string }) =>
      foodApi.cancel(id, version, reason),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ordersQueryKey });
      void queryClient.invalidateQueries({ queryKey: orderQueryKey(id) });
    },
  });
}
