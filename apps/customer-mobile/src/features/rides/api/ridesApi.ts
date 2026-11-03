import { apiRequest } from '../../../api/apiClient';

// Shapes returned by /api/v1/rides (spec Phase 9). Enums arrive in camelCase.

export type VehicleClass = 'auto' | 'cab' | 'bikeTaxi';

export type RideStatus =
  'requested' | 'accepted' | 'arrived' | 'inProgress' | 'completed' | 'cancelled' | 'expired';

export interface PlaceChoice {
  latitude: number;
  longitude: number;
  label: string;
}

export interface QuoteOption {
  quoteId: string;
  vehicleClass: VehicleClass;
  distanceKm: number;
  fare: number;
  currency: string;
  expiresAt: string;
  cancellationTerms: string;
  /** Whether an eligible driver is close enough right now. */
  available: boolean;
}

export interface Quotes {
  pickupLabel: string;
  destinationLabel: string;
  options: QuoteOption[];
}

export interface Ride {
  id: string;
  status: RideStatus;
  vehicleClass: VehicleClass;
  pickup: PlaceChoice;
  destination: PlaceChoice;
  pickupNote: string | null;
  distanceKm: number;
  fare: number;
  currency: string;
  cancellationTerms: string;
  requestedAt: string;
  searchEndsAt: string;
  cancelledBy: 'rider' | 'driver' | 'system' | null;
  cancelReason: string | null;
  /** A first name and a vehicle: never a phone number. */
  driver: { firstName: string; vehicleClass: VehicleClass; vehicleNumber: string } | null;
  /** Only while a driver has the ride. */
  driverLocation: { latitude: number; longitude: number; at: string } | null;
  version: number;
}

export type IncidentCategory =
  'unsafeDriving' | 'harassment' | 'accident' | 'vehicleProblem' | 'overcharge' | 'other';

export const incidentCategories: readonly IncidentCategory[] = [
  'unsafeDriving',
  'harassment',
  'accident',
  'vehicleProblem',
  'overcharge',
  'other',
];

export interface SafetyReceipt {
  id: string;
  kind: 'sos' | 'incident';
  status: string;
  emergencyNumber: string;
}

export interface ShareLink {
  token: string;
  expiresAt: string;
}

const ride = (id: string) => `/api/v1/rides/${encodeURIComponent(id)}`;

export const ridesApi = {
  quote: (pickup: PlaceChoice, destination: PlaceChoice) =>
    apiRequest<Quotes>('/api/v1/rides/quotes', {
      method: 'POST',
      auth: true,
      body: {
        pickupLatitude: pickup.latitude,
        pickupLongitude: pickup.longitude,
        pickupLabel: pickup.label,
        destinationLatitude: destination.latitude,
        destinationLongitude: destination.longitude,
        destinationLabel: destination.label,
      },
    }),

  request: (quoteId: string, pickupNote?: string) =>
    apiRequest<Ride>('/api/v1/rides', {
      method: 'POST',
      auth: true,
      body: { quoteId, ...(pickupNote ? { pickupNote } : {}) },
    }),

  /** The ride in progress, or the one that ended in the last few minutes; null when there is none. */
  current: async () =>
    (await apiRequest<Ride | undefined>('/api/v1/rides/current', { auth: true })) ?? null,

  cancel: (id: string, reason?: string) =>
    apiRequest<Ride>(`${ride(id)}/cancel`, { method: 'POST', auth: true, body: { reason } }),

  sos: (id: string, position: { latitude: number; longitude: number } | null) =>
    apiRequest<SafetyReceipt>(`${ride(id)}/sos`, {
      method: 'POST',
      auth: true,
      body: position ?? {},
    }),

  incident: (id: string, category: IncidentCategory, details: string) =>
    apiRequest<SafetyReceipt>(`${ride(id)}/incidents`, {
      method: 'POST',
      auth: true,
      body: { category, details },
    }),

  share: (id: string) => apiRequest<ShareLink>(`${ride(id)}/share`, { method: 'POST', auth: true }),
};
