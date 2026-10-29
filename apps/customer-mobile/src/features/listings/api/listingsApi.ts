import { apiRequest } from '../../../api/apiClient';

// Shapes returned by GET /api/v1/listings (spec §10, Phase 6). Enums arrive in camelCase.

export type ListingType = 'activity' | 'stay' | 'event' | 'service' | 'shop';

export interface CancellationPolicy {
  freeCancellationHours: number;
  refundPercentage: number;
  notes: string | null;
}

export interface ListingSummary {
  id: string;
  businessName: string;
  listingType: ListingType;
  title: string;
  isBookable: boolean;
  basePrice: number | null;
  currency: string;
}

export interface ListingDetail {
  id: string;
  businessId: string;
  businessName: string;
  businessAddress: string | null;
  listingType: ListingType;
  title: string;
  description: string | null;
  isBookable: boolean;
  basePrice: number | null;
  currency: string;
  cancellationPolicy: CancellationPolicy | null;
}

export interface AvailabilitySlot {
  id: string;
  listingId: string;
  version: number;
  startAt: string;
  endAt: string;
  capacity: number;
  reservedCount: number;
  availableCount: number;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ListingsQuery {
  type?: ListingType;
  page?: number;
  pageSize?: number;
}

function queryString(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined) {
      search.set(key, String(value));
    }
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

export const listingsApi = {
  list: (query: ListingsQuery = {}) =>
    apiRequest<Paged<ListingSummary>>(`/api/v1/listings${queryString({ ...query })}`),

  get: (id: string) => apiRequest<ListingDetail>(`/api/v1/listings/${encodeURIComponent(id)}`),

  availability: (id: string, from?: string) =>
    apiRequest<AvailabilitySlot[]>(
      `/api/v1/listings/${encodeURIComponent(id)}/availability${queryString({ from })}`,
    ),
};
