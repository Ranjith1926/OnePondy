import { apiRequest } from '../../../api/apiClient';

// Shapes returned by /api/v1/ride-driver (spec Phase 9). Enums arrive in camelCase.

export const vehicleClasses = ['auto', 'cab', 'bikeTaxi'] as const;
export type VehicleClass = (typeof vehicleClasses)[number];

export type ApprovalStatus = 'pending' | 'approved' | 'rejected' | 'suspended';

/** The driver's own view of their ride application. Everything but `configured` is empty until they apply. */
export interface RideDriverProfile {
  configured: boolean;
  userId: string;
  fullName: string | null;
  vehicleClass: VehicleClass | null;
  vehicleNumber: string | null;
  licenceNumber: string | null;
  permitNumber: string | null;
  insurancePolicyNumber: string | null;
  /** The last day the policy covers, as YYYY-MM-DD. */
  insuranceValidUntil: string | null;
  approvalStatus: ApprovalStatus | null;
  reviewNote: string | null;
  isOnDuty: boolean;
  onDutySince: string | null;
  /** Approved and insured today. */
  eligible: boolean;
  insuranceEnded: boolean;
  version: number;
}

export interface RideDriverDetails {
  fullName: string;
  vehicleClass: VehicleClass;
  vehicleNumber: string;
  licenceNumber: string;
  permitNumber: string;
  insurancePolicyNumber: string;
  insuranceValidUntil: string;
  /** Required once the driver has applied: the version they were looking at. */
  version?: number;
}

export type RideStatus =
  'requested' | 'accepted' | 'arrived' | 'inProgress' | 'completed' | 'cancelled' | 'expired';

export interface Place {
  latitude: number;
  longitude: number;
  label: string;
}

/** A request before it is accepted: the name of the pickup and how far it is, never the exact point, the rider or the note. */
export interface RideOffer {
  id: string;
  vehicleClass: VehicleClass;
  pickupLabel: string;
  pickupDistanceKm: number;
  destinationLabel: string;
  distanceKm: number;
  fare: number;
  currency: string;
  requestedAt: string;
}

/** A ride the driver holds: now the exact pickup, the note and the rider's first name (never a phone number). */
export interface DriverRide {
  id: string;
  status: RideStatus;
  vehicleClass: VehicleClass;
  riderFirstName: string;
  pickup: Place;
  destination: Place;
  pickupNote: string | null;
  distanceKm: number;
  fare: number;
  currency: string;
  acceptedAt: string | null;
  arrivedAt: string | null;
  startedAt: string | null;
  version: number;
}

export interface DriverRides {
  current: DriverRide | null;
  offers: RideOffer[];
  /** False while off duty or not eligible, which is why `offers` is then empty. */
  available: boolean;
}

export type IncidentCategory =
  'unsafeDriving' | 'harassment' | 'accident' | 'vehicleProblem' | 'overcharge' | 'other';

export const incidentCategories: readonly IncidentCategory[] = [
  'harassment',
  'unsafeDriving',
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

const ride = (id: string) => `/api/v1/ride-driver/rides/${encodeURIComponent(id)}`;
const post = <T>(path: string, body?: unknown) =>
  apiRequest<T>(path, { method: 'POST', body, auth: true });

export const rideDriverApi = {
  getMine: () => apiRequest<RideDriverProfile>('/api/v1/ride-driver/me', { auth: true }),

  save: (details: RideDriverDetails) =>
    apiRequest<RideDriverProfile>('/api/v1/ride-driver/me', {
      method: 'PUT',
      body: details,
      auth: true,
    }),

  setDuty: (onDuty: boolean, version: number) =>
    post<RideDriverProfile>('/api/v1/ride-driver/duty', { onDuty, version }),

  /** Where the driver is, for dispatch: only while on duty, and only the latest is kept. */
  shareLocation: (latitude: number, longitude: number) =>
    post<void>('/api/v1/ride-driver/location', { latitude, longitude }),

  rides: () => apiRequest<DriverRides>('/api/v1/ride-driver/rides', { auth: true }),

  accept: (id: string) => post<DriverRide>(`${ride(id)}/accept`),
  giveUp: (id: string) => post<DriverRide>(`${ride(id)}/give-up`),
  arrive: (id: string) => post<DriverRide>(`${ride(id)}/arrive`),
  start: (id: string) => post<DriverRide>(`${ride(id)}/start`),
  complete: (id: string, cashCollected: number) =>
    post<DriverRide>(`${ride(id)}/complete`, { cashCollected }),
  cancel: (id: string, reason: string) => post<DriverRide>(`${ride(id)}/cancel`, { reason }),

  /** The driver's position on the ride the rider is following: only while they hold it. */
  shareRideLocation: (id: string, latitude: number, longitude: number) =>
    post<void>(`${ride(id)}/location`, { latitude, longitude }),

  sos: (id: string, position: { latitude: number; longitude: number } | null) =>
    post<SafetyReceipt>(`${ride(id)}/sos`, position ?? {}),

  incident: (id: string, category: IncidentCategory, details: string) =>
    post<SafetyReceipt>(`${ride(id)}/incidents`, { category, details }),
};
