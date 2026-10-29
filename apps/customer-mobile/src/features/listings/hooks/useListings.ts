import { useQuery } from '@tanstack/react-query';

import { listingsApi, type ListingsQuery } from '../api/listingsApi';

export const listingsQueryKey = (query: ListingsQuery) => ['listings', query] as const;

/** Published, bookable or discovery-only listings (spec §10, Phase 6): what partners offer. */
export function useListings(query: ListingsQuery = {}) {
  return useQuery({
    queryKey: listingsQueryKey(query),
    queryFn: () => listingsApi.list(query),
  });
}

export function useListing(id: string | undefined) {
  return useQuery({
    queryKey: ['listing', id] as const,
    queryFn: () => listingsApi.get(id!),
    enabled: id !== undefined,
  });
}

export function useListingAvailability(id: string | undefined) {
  return useQuery({
    queryKey: ['listing-availability', id] as const,
    queryFn: () => listingsApi.availability(id!, new Date().toISOString()),
    enabled: id !== undefined,
  });
}
