import type { QuoteOption, Quotes, Ride } from '../features/rides/api/ridesApi';

export const rideId = 'e0000000-0000-4000-8000-000000000001';

export function aRide(overrides: Partial<Ride> = {}): Ride {
  return {
    id: rideId,
    status: 'requested',
    vehicleClass: 'auto',
    pickup: { latitude: 11.9343, longitude: 79.8369, label: 'Promenade Beach' },
    destination: { latitude: 12.0053, longitude: 79.81, label: 'Auroville (Matrimandir)' },
    pickupNote: null,
    distanceKm: 11.5,
    fare: 203,
    currency: 'INR',
    cancellationTerms: 'You can cancel free of charge until your trip starts.',
    requestedAt: '2026-11-03T08:00:00Z',
    searchEndsAt: '2026-11-03T08:10:00Z',
    cancelledBy: null,
    cancelReason: null,
    driver: null,
    driverLocation: null,
    version: 1,
    ...overrides,
  };
}

export function withDriver(overrides: Partial<Ride> = {}): Ride {
  return aRide({
    status: 'accepted',
    driver: { firstName: 'Ravi', vehicleClass: 'auto', vehicleNumber: 'PY01AB1234' },
    ...overrides,
  });
}

export function aQuote(overrides: Partial<QuoteOption> = {}): QuoteOption {
  return {
    quoteId: 'f0000000-0000-4000-8000-000000000001',
    vehicleClass: 'auto',
    distanceKm: 11.5,
    fare: 203,
    currency: 'INR',
    expiresAt: '2026-11-03T08:05:00Z',
    cancellationTerms: 'You can cancel free of charge until your trip starts.',
    available: true,
    ...overrides,
  };
}

export function someQuotes(options: QuoteOption[] = [aQuote()]): Quotes {
  return { pickupLabel: 'Promenade Beach', destinationLabel: 'Auroville (Matrimandir)', options };
}
