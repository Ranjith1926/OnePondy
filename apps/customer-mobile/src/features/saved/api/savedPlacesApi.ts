import { apiRequest } from '../../../api/apiClient';
import type { PlaceSummary } from '../../tourism/api/placesApi';

export interface SavedPlace {
  place: PlaceSummary;
  savedAt: string;
}

export const savedPlacesApi = {
  list: (lang: string) =>
    apiRequest<SavedPlace[]>(`/api/v1/saved-places?lang=${encodeURIComponent(lang)}`, {
      auth: true,
    }),

  save: (placeId: string) =>
    apiRequest<void>('/api/v1/saved-places', { method: 'POST', body: { placeId }, auth: true }),

  remove: (placeId: string) =>
    apiRequest<void>(`/api/v1/saved-places/${encodeURIComponent(placeId)}`, {
      method: 'DELETE',
      auth: true,
    }),
};
