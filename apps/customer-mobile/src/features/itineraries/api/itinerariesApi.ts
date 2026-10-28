import { apiRequest } from '../../../api/apiClient';
import type { PlaceSummary } from '../../tourism/api/placesApi';

export type ItineraryStatus = 'draft' | 'saved' | 'archived';
export type ItinerarySource = 'manual' | 'aiAssisted';

export interface ItinerarySummary {
  id: string;
  title: string;
  startDate: string | null;
  endDate: string | null;
  status: ItineraryStatus;
  source: ItinerarySource;
  isShared: boolean;
  stopCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ItineraryStop {
  id: string;
  dayNumber: number;
  stopOrder: number;
  notes: string | null;
  plannedArrival: string | null;
  place: PlaceSummary;
}

export interface ItineraryDetail {
  id: string;
  title: string;
  startDate: string | null;
  endDate: string | null;
  status: ItineraryStatus;
  source: ItinerarySource;
  /** Null while sharing is off. */
  shareToken: string | null;
  createdAt: string;
  updatedAt: string;
  stops: ItineraryStop[];
}

export interface SharedItinerary {
  title: string;
  startDate: string | null;
  endDate: string | null;
  stops: ItineraryStop[];
}

export interface ItineraryInput {
  title: string;
  startDate?: string | null;
  endDate?: string | null;
}

export interface AddStopInput {
  placeId: string;
  dayNumber?: number;
  notes?: string | null;
  plannedArrival?: string | null;
}

export interface StopPosition {
  stopId: string;
  dayNumber: number;
  stopOrder: number;
}

const base = '/api/v1/itineraries';
const withLang = (lang: string) => `lang=${encodeURIComponent(lang)}`;

export const itinerariesApi = {
  list: (includeArchived: boolean) =>
    apiRequest<ItinerarySummary[]>(`${base}?includeArchived=${includeArchived}`, { auth: true }),

  get: (id: string, lang: string) =>
    apiRequest<ItineraryDetail>(`${base}/${id}?${withLang(lang)}`, { auth: true }),

  create: (input: ItineraryInput, lang: string) =>
    apiRequest<ItineraryDetail>(`${base}?${withLang(lang)}`, {
      method: 'POST',
      body: input,
      auth: true,
    }),

  update: (id: string, input: ItineraryInput, lang: string) =>
    apiRequest<ItineraryDetail>(`${base}/${id}?${withLang(lang)}`, {
      method: 'PUT',
      body: input,
      auth: true,
    }),

  remove: (id: string) => apiRequest<void>(`${base}/${id}`, { method: 'DELETE', auth: true }),

  archive: (id: string, lang: string) =>
    apiRequest<ItineraryDetail>(`${base}/${id}/archive?${withLang(lang)}`, {
      method: 'POST',
      auth: true,
    }),

  unarchive: (id: string, lang: string) =>
    apiRequest<ItineraryDetail>(`${base}/${id}/unarchive?${withLang(lang)}`, {
      method: 'POST',
      auth: true,
    }),

  addStop: (id: string, input: AddStopInput, lang: string) =>
    apiRequest<ItineraryDetail>(`${base}/${id}/stops?${withLang(lang)}`, {
      method: 'POST',
      body: input,
      auth: true,
    }),

  removeStop: (id: string, stopId: string, lang: string) =>
    apiRequest<ItineraryDetail>(`${base}/${id}/stops/${stopId}?${withLang(lang)}`, {
      method: 'DELETE',
      auth: true,
    }),

  reorderStops: (id: string, positions: StopPosition[], lang: string) =>
    apiRequest<ItineraryDetail>(`${base}/${id}/stops/order?${withLang(lang)}`, {
      method: 'PUT',
      body: { positions },
      auth: true,
    }),

  enableSharing: (id: string) =>
    apiRequest<{ shareToken: string }>(`${base}/${id}/share`, { method: 'POST', auth: true }),

  disableSharing: (id: string) =>
    apiRequest<void>(`${base}/${id}/share`, { method: 'DELETE', auth: true }),

  /** Public: no session needed, shows the plan but never who made it. */
  getShared: (token: string, lang: string) =>
    apiRequest<SharedItinerary>(`${base}/shared/${encodeURIComponent(token)}?${withLang(lang)}`),
};
