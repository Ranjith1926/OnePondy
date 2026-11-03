import { isApiError, isNetworkError } from '../../api/apiErrors';

const knownCodes = [
  'review_not_eligible',
  'review_window_closed',
  'review_already_exists',
  'review_not_editable',
  'review_already_edited',
  'review_edit_window_closed',
  'review_changed',
  'review_transaction_not_found',
  'review_not_found',
  'validation_failed',
] as const;

type KnownCode = (typeof knownCodes)[number];

export type ReviewErrorMessageKey =
  `reviews.errors.${KnownCode | 'offline' | 'timeout' | 'generic'}`;

function isKnownCode(code: string): code is KnownCode {
  return (knownCodes as readonly string[]).includes(code);
}

/** Maps a review failure to a translatable message, so the customer is told what happened and what to do. */
export function reviewErrorMessageKey(error: unknown): ReviewErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'reviews.errors.timeout' : 'reviews.errors.offline';
  }

  if (isApiError(error) && isKnownCode(error.code)) {
    return `reviews.errors.${error.code}`;
  }

  return 'reviews.errors.generic';
}
