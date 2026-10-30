import { isApiError, isNetworkError } from '../../../api/apiErrors';

const knownCodes = [
  'driver_changed',
  'driver_locked',
  'driver_licence_in_use',
  'driver_not_approved',
  'driver_not_applied',
  'driver_invalid_transition',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type DriverErrorMessageKey =
  `driver.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps an API or network failure to a translatable message key. */
export function driverErrorMessageKey(error: unknown): DriverErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'driver.errors.timeout' : 'driver.errors.offline';
  }
  if (isApiError(error) && isKnownCode(error.code)) {
    return `driver.errors.${error.code}`;
  }
  return 'driver.errors.generic';
}
