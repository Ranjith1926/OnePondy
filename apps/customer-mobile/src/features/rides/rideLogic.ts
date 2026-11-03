import type { PlaceChoice, Ride, RideStatus } from './api/ridesApi';

/**
 * Places to choose from until the app has a map picker. These are well-known points in and around Puducherry; the coordinates are
 * approximate and should be checked by someone who knows the town before launch (docs/phase-9-plan.md).
 */
export const landmarks: readonly (PlaceChoice & { id: string })[] = [
  { id: 'promenade', label: 'Promenade Beach', latitude: 11.9343, longitude: 79.8369 },
  { id: 'whiteTown', label: 'White Town', latitude: 11.9341, longitude: 79.8338 },
  { id: 'ashram', label: 'Sri Aurobindo Ashram', latitude: 11.9337, longitude: 79.8355 },
  { id: 'station', label: 'Puducherry Railway Station', latitude: 11.9243, longitude: 79.8239 },
  { id: 'paradise', label: 'Paradise Beach', latitude: 11.9066, longitude: 79.8184 },
  { id: 'auroville', label: 'Auroville (Matrimandir)', latitude: 12.0053, longitude: 79.81 },
];

/** A ride that has not ended: searching, or with a driver. */
export const isOpen = (status: RideStatus): boolean =>
  status === 'requested' ||
  status === 'accepted' ||
  status === 'arrived' ||
  status === 'inProgress';

/** The rider can cancel until the trip starts. */
export const canCancel = (status: RideStatus): boolean =>
  status === 'requested' || status === 'accepted' || status === 'arrived';

/** The emergency button, sharing and the driver's position make sense only while a driver is with the rider. */
export const hasDriver = (status: RideStatus): boolean =>
  status === 'accepted' || status === 'arrived' || status === 'inProgress';

/** Something that happened on a ride can be reported once a driver had taken it. */
export const canReport = (ride: Ride): boolean => ride.driver !== null;

/** How often the ride is re-read: quickly while the socket is down, slowly as a safety net while it is up, never once it has ended. */
export function refetchInterval(ride: Ride | null | undefined, connected: boolean): number | false {
  if (!ride || !isOpen(ride.status)) {
    return false;
  }

  return connected ? 60_000 : 10_000;
}

export const rupees = (amount: number): string =>
  `₹${Number.isInteger(amount) ? amount : amount.toFixed(2)}`;
