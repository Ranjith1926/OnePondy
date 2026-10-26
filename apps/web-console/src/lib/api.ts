import 'server-only';

import { redirect } from 'next/navigation';

import { apiBaseUrl } from './config';
import { getSession } from './session';

/** An API problem response: a stable code, a readable detail and, for forms, errors per field. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

interface ProblemBody {
  code?: string;
  detail?: string;
  title?: string;
  errors?: Record<string, string[]>;
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemBody = {};
  try {
    problem = (await response.json()) as ProblemBody;
  } catch {
    // Not JSON: keep the status only.
  }
  return new ApiError(
    response.status,
    problem.code ?? `http_${response.status}`,
    problem.detail ?? problem.title ?? 'The server could not complete the request.',
    problem.errors ?? {},
  );
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  json?: unknown;
  form?: FormData;
}

async function send(
  path: string,
  options: RequestOptions,
  token: string | null,
): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  if (options.json !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  return fetch(`${apiBaseUrl()}${path}`, {
    method: options.method ?? 'GET',
    headers,
    body: options.form ?? (options.json === undefined ? undefined : JSON.stringify(options.json)),
    cache: 'no-store',
  });
}

async function read<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw await toApiError(response);
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

/**
 * Calls the API as the signed-in staff member. The proxy keeps the access token fresh, so a 401
 * here means the session was revoked (signed out elsewhere or role changed).
 */
export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const session = await getSession();
  if (!session) {
    redirect('/sign-in');
  }

  const response = await send(path, options, session.accessToken);
  if (response.status === 401) {
    redirect('/session-expired');
  }
  return read<T>(response);
}

/** Calls endpoints that need no session, such as sign-in. */
export async function publicApi<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return read<T>(await send(path, options, null));
}

/** Calls an endpoint with an explicit token, e.g. right after sign-in before cookies are readable. */
export async function apiWithToken<T>(
  path: string,
  token: string,
  options: RequestOptions = {},
): Promise<T> {
  return read<T>(await send(path, options, token));
}
