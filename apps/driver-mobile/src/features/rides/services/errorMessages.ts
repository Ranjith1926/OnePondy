import { isApiError, isNetworkError } from '../../../api/apiErrors';

const knownCodes = [
  'ride_driver_changed',
  'ride_driver_locked',
  'ride_driver_document_in_use',
  'ride_driver_not_approved',
  'ride_driver_not_applied',
  'ride_driver_insurance_ended',
  'ride_driver_has_active_ride',
  'ride_driver_not_on_duty',
  'ride_driver_not_available',
  'ride_driver_busy',
  'ride_taken',
  'ride_own_request',
  'ride_too_far',
  'ride_changed',
  'ride_not_found',
  'ride_invalid_transition',
  'ride_not_active',
  'ride_report_limit',
  'ride_report_window_passed',
  'validation_failed',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type RideErrorMessageKey = `rides.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps an API or network failure to a translatable message key. */
export function rideErrorMessageKey(error: unknown): RideErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'rides.errors.timeout' : 'rides.errors.offline';
  }

  if (isApiError(error) && isKnownCode(error.code)) {
    return `rides.errors.${error.code}`;
  }

  return 'rides.errors.generic';
}
