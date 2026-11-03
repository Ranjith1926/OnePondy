import { isApiError, isNetworkError } from '../../api/apiErrors';

const knownCodes = [
  'rides_unavailable',
  'ride_quote_not_found',
  'ride_quote_expired',
  'ride_quote_used',
  'ride_class_unavailable',
  'ride_no_drivers',
  'ride_rider_busy',
  'ride_not_found',
  'ride_invalid_transition',
  'ride_changed',
  'ride_not_active',
  'ride_report_limit',
  'ride_report_window_passed',
  'ride_no_driver_yet',
  'ride_share_unavailable',
  'validation_failed',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type RideErrorMessageKey = `rides.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps a ride failure to a translatable message, so the rider is told what happened and what to do. */
export function rideErrorMessageKey(error: unknown): RideErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'rides.errors.timeout' : 'rides.errors.offline';
  }

  if (isApiError(error) && isKnownCode(error.code)) {
    return `rides.errors.${error.code}`;
  }

  return 'rides.errors.generic';
}
