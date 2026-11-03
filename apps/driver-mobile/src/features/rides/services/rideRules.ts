import type { RideStatus } from '../api/rideDriverApi';

/** Whether a date typed as YYYY-MM-DD is a real date that has not passed (the day it ends still counts). */
export function isValidInsuranceDate(text: string, today: Date = new Date()): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text.trim());
  if (!match) {
    return false;
  }

  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  ) {
    return false;
  }

  const startOfToday = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());
  return date.getTime() >= startOfToday;
}

/** What the driver can do next on a ride they hold. The server decides; this only chooses which buttons to show. */
export function nextStep(status: RideStatus): 'arrive' | 'start' | 'complete' | null {
  switch (status) {
    case 'accepted':
      return 'arrive';
    case 'arrived':
      return 'start';
    case 'inProgress':
      return 'complete';
    default:
      return null;
  }
}

/** Giving a ride up is allowed until arriving; after that, the driver can only end it with a reason. */
export const canGiveUp = (status: RideStatus): boolean => status === 'accepted';
export const canEndWithReason = (status: RideStatus): boolean => status === 'arrived';

export const rupees = (amount: number): string =>
  `₹${Number.isInteger(amount) ? amount : amount.toFixed(2)}`;

/** Tidies a registration, licence, permit or policy number the way the server does: upper case, no spaces, hyphens or slashes. */
export const tidyNumber = (value: string): string => value.replace(/[\s\-/]/g, '').toUpperCase();
