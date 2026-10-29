import { apiRequest } from '../../../api/apiClient';
import type { ListingSummary } from './listingsApi';

export interface SavedListing {
  listing: ListingSummary;
  savedAt: string;
}

export const savedListingsApi = {
  list: () => apiRequest<SavedListing[]>('/api/v1/saved-listings', { auth: true }),

  save: (listingId: string) =>
    apiRequest<void>('/api/v1/saved-listings', { method: 'POST', body: { listingId }, auth: true }),

  remove: (listingId: string) =>
    apiRequest<void>(`/api/v1/saved-listings/${encodeURIComponent(listingId)}`, {
      method: 'DELETE',
      auth: true,
    }),
};
