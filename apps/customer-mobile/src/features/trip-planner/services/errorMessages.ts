import { isApiError, isNetworkError } from '../../../api/apiErrors';

const codeToKey = {
  trip_planner_disabled: 'tripPlanner.errors.disabled',
  trip_planner_provider_unavailable: 'tripPlanner.errors.providerUnavailable',
  trip_planner_daily_cap_reached: 'tripPlanner.errors.dailyCapReached',
  trip_planner_rate_limited: 'tripPlanner.errors.rateLimited',
  trip_planner_no_candidates: 'tripPlanner.errors.noCandidates',
} as const;

type KnownCode = keyof typeof codeToKey;

export type TripPlannerErrorMessageKey =
  | (typeof codeToKey)[KnownCode]
  | 'tripPlanner.errors.offline'
  | 'tripPlanner.errors.timeout'
  | 'tripPlanner.errors.validation'
  | 'tripPlanner.errors.generic';

function isKnownCode(code: string): code is KnownCode {
  return Object.hasOwn(codeToKey, code);
}

/** Maps a trip plan failure to a translatable message key; most of these mean "fall back to the manual builder". */
export function tripPlannerErrorMessageKey(error: unknown): TripPlannerErrorMessageKey {
  if (isNetworkError(error)) {
    return error.reason === 'timeout' ? 'tripPlanner.errors.timeout' : 'tripPlanner.errors.offline';
  }

  if (isApiError(error)) {
    if (isKnownCode(error.code)) {
      return codeToKey[error.code];
    }

    if (error.code === 'validation_failed') {
      return 'tripPlanner.errors.validation';
    }
  }

  return 'tripPlanner.errors.generic';
}
