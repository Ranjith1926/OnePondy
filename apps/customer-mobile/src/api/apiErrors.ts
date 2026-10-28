/** An HTTP error response, carrying the API's machine-readable problem `code` (spec §8). */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly retryAfterSeconds?: number;
  readonly reasons?: string[];
  /** Validation problems keyed by field name, e.g. {"title": ["Required."]}. */
  readonly fieldErrors?: Record<string, string[]>;

  constructor(
    status: number,
    code: string,
    retryAfterSeconds?: number,
    reasons?: string[],
    fieldErrors?: Record<string, string[]>,
  ) {
    super(code);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.retryAfterSeconds = retryAfterSeconds;
    this.reasons = reasons;
    this.fieldErrors = fieldErrors;
  }
}

/** The request never got a response: the device is offline or the server timed out. */
export class NetworkError extends Error {
  readonly reason: 'offline' | 'timeout';

  constructor(reason: 'offline' | 'timeout') {
    super(reason);
    this.name = 'NetworkError';
    this.reason = reason;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export function isNetworkError(error: unknown): error is NetworkError {
  return error instanceof NetworkError;
}
