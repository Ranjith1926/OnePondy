import { isApiError, isNetworkError } from '../../../api/apiErrors';

const knownCodes = [
  'invalid_destination',
  'unsupported_country',
  'too_many_requests',
  'rate_limited',
  'codes_unavailable',
  'delivery_unavailable',
  'invalid_code',
  'code_expired',
  'too_many_attempts',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type AuthErrorMessageKey = `auth.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps an API or network failure to a translatable message key. */
export function authErrorMessageKey(error: unknown): AuthErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'auth.errors.timeout' : 'auth.errors.offline';
  }
  if (isApiError(error) && isKnownCode(error.code)) {
    return `auth.errors.${error.code}`;
  }
  return 'auth.errors.generic';
}
