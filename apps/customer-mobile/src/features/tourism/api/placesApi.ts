import { apiRequest } from '../../../api/apiClient';
import { apiBaseUrl } from '../../../api/config';

// Shapes returned by GET /api/v1/places (spec §10, Phase 4). Enums arrive in camelCase.

export type VisitorCategory =
  'standard' | 'indianAdult' | 'foreignAdult' | 'child' | 'student' | 'senior';

export type DayOfWeekKey =
  'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

export interface OpeningPeriod {
  day: DayOfWeekKey;
  opens: string;
  closes: string;
}

export interface OpeningHours {
  alwaysOpen: boolean;
  periods: OpeningPeriod[];
  notes: string | null;
}

export interface EntryFee {
  category: VisitorCategory;
  amount: number;
  currency: string;
}

export interface PlaceImage {
  altText: string;
  caption: string | null;
  attribution: string | null;
  width: number;
  height: number;
  urls: { large: string; medium: string; thumb: string };
}

export interface PlaceSummary {
  id: string;
  slug: string;
  name: string;
  categoryCode: string;
  latitude: number | null;
  longitude: number | null;
  coverImage: PlaceImage | null;
  /** Present only when the list was fetched with coordinates (useLocation hooks). */
  distanceKm: number | null;
}

export interface PlaceDetail {
  id: string;
  slug: string;
  language: string;
  name: string;
  categoryCode: string;
  description: string | null;
  visitorGuidance: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  openingHours: OpeningHours | null;
  entryFees: EntryFee[];
  images: PlaceImage[];
  verifiedAt: string | null;
  lastReviewedAt: string | null;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PlacesQuery {
  category?: string;
  q?: string;
  lang?: string;
  lat?: number;
  lng?: number;
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

/** The API returns media paths relative to its own host (e.g. "/media/..."); RN needs a full URL. */
export function resolveMediaUrl(path: string): string {
  return /^https?:\/\//.test(path) ? path : `${apiBaseUrl}${path}`;
}

export const placesApi = {
  list: (query: PlacesQuery = {}) =>
    apiRequest<Paged<PlaceSummary>>(`/api/v1/places${queryString({ ...query })}`),

  /** React Native's fetch does not reliably send Accept-Language, so the app's own language is passed explicitly. */
  get: (idOrSlug: string, lang: string) =>
    apiRequest<PlaceDetail>(
      `/api/v1/places/${encodeURIComponent(idOrSlug)}${queryString({ lang })}`,
    ),
};
