import { isApiError, isNetworkError } from '../../api/apiErrors';

const knownCodes = [
  'support_unavailable',
  'support_too_many_open',
  'support_too_many_messages',
  'support_cannot_reopen',
  'support_linked_not_found',
  'ticket_not_found',
  'support_changed',
  'validation_failed',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type SupportErrorMessageKey =
  `support.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps a support failure to a translatable message, so the customer is told what happened and what to do. */
export function supportErrorMessageKey(error: unknown): SupportErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'support.errors.timeout' : 'support.errors.offline';
  }

  if (isApiError(error) && isKnownCode(error.code)) {
    return `support.errors.${error.code}`;
  }

  return 'support.errors.generic';
}
