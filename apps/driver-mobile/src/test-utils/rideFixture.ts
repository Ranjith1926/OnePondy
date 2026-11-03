import type { DriverRide, RideDriverProfile, RideOffer } from '../features/rides/api/rideDriverApi';

export const rideId = 'e0000000-0000-4000-8000-000000000001';

export function aProfile(overrides: Partial<RideDriverProfile> = {}): RideDriverProfile {
  return {
    configured: true,
    userId: 'u1',
    fullName: 'Ravi Kumar',
    vehicleClass: 'auto',
    vehicleNumber: 'PY01AB1234',
    licenceNumber: 'PY0120200012345',
    permitNumber: 'PERMIT0001',
    insurancePolicyNumber: 'POLICY778899',
    insuranceValidUntil: '2027-03-31',
    approvalStatus: 'approved',
    reviewNote: null,
    isOnDuty: false,
    onDutySince: null,
    eligible: true,
    insuranceEnded: false,
    version: 3,
    ...overrides,
  };
}

export const notApplied = aProfile({
  configured: false,
  fullName: null,
  vehicleClass: null,
  vehicleNumber: null,
  licenceNumber: null,
  permitNumber: null,
  insurancePolicyNumber: null,
  insuranceValidUntil: null,
  approvalStatus: null,
  eligible: false,
  version: 0,
});

export function anOffer(overrides: Partial<RideOffer> = {}): RideOffer {
  return {
    id: rideId,
    vehicleClass: 'auto',
    pickupLabel: 'Promenade Beach',
    pickupDistanceKm: 0.4,
    destinationLabel: 'Auroville',
    distanceKm: 11.5,
    fare: 203,
    currency: 'INR',
    requestedAt: '2026-11-03T08:00:00Z',
    ...overrides,
  };
}

export function aRide(overrides: Partial<DriverRide> = {}): DriverRide {
  return {
    id: rideId,
    status: 'accepted',
    vehicleClass: 'auto',
    riderFirstName: 'Anita',
    pickup: { latitude: 11.9343, longitude: 79.8369, label: 'Promenade Beach' },
    destination: { latitude: 12.0053, longitude: 79.81, label: 'Auroville' },
    pickupNote: 'Blue gate',
    distanceKm: 11.5,
    fare: 203,
    currency: 'INR',
    acceptedAt: '2026-11-03T08:01:00Z',
    arrivedAt: null,
    startedAt: null,
    version: 2,
    ...overrides,
  };
}
