import { ApiError, NetworkError } from './apiErrors';
import { apiBaseUrl, requestTimeoutMs } from './config';

/** Supplied by the auth feature so this module never imports it (avoids a dependency cycle). */
export interface ApiAuthHooks {
  /** A valid access token, refreshed first if it is about to expire; null when signed out. */
  getAccessToken: () => Promise<string | null>;
  /** Forces a refresh; null when the session has ended. */
  refreshAccessToken: () => Promise<string | null>;
}

let authHooks: ApiAuthHooks | null = null;

export function configureApiAuth(hooks: ApiAuthHooks | null): void {
  authHooks = hooks;
}

export interface ApiRequestOptions {
  method?: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE';
  body?: unknown;
  /** Attach the access token, and refresh and retry once on a 401. */
  auth?: boolean;
  timeoutMs?: number;
}

export async function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  if (!options.auth) {
    return parse<T>(await send(path, options, null));
  }

  const token = await authHooks?.getAccessToken();
  if (!token) {
    throw new ApiError(401, 'not_signed_in');
  }

  const response = await send(path, options, token);
  if (response.status !== 401 || !authHooks) {
    return parse<T>(response);
  }

  const refreshed = await authHooks.refreshAccessToken();
  if (!refreshed) {
    throw new ApiError(401, 'session_expired');
  }
  return parse<T>(await send(path, options, refreshed));
}

async function send(
  path: string,
  options: ApiRequestOptions,
  token: string | null,
): Promise<Response> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), options.timeoutMs ?? requestTimeoutMs);
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  try {
    return await fetch(`${apiBaseUrl}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: controller.signal,
    });
  } catch {
    throw new NetworkError(controller.signal.aborted ? 'timeout' : 'offline');
  } finally {
    clearTimeout(timer);
  }
}

interface ProblemBody {
  code?: string;
  retryAfterSeconds?: number;
  reasons?: string[];
  errors?: Record<string, string[]>;
}

async function parse<T>(response: Response): Promise<T> {
  const text = await response.text();
  let data: unknown;
  try {
    data = text ? JSON.parse(text) : undefined;
  } catch {
    data = undefined;
  }

  if (!response.ok) {
    const problem = (data ?? {}) as ProblemBody;
    throw new ApiError(
      response.status,
      problem.code ?? `http_${response.status}`,
      problem.retryAfterSeconds,
      problem.reasons,
      problem.errors,
    );
  }
  return data as T;
}
