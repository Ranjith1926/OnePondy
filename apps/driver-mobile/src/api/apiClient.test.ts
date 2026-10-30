import { apiRequest, configureApiAuth } from './apiClient';
import { ApiError, NetworkError } from './apiErrors';

function reply(status: number, body?: unknown): Response {
  const text = body === undefined ? '' : JSON.stringify(body);
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(text),
  } as Response;
}

const fetchMock = jest.fn<Promise<Response>, [string, RequestInit]>();
const originalFetch = globalThis.fetch;

beforeEach(() => {
  fetchMock.mockReset();
  globalThis.fetch = fetchMock as unknown as typeof fetch;
  configureApiAuth(null);
});

afterAll(() => {
  globalThis.fetch = originalFetch;
});

describe('apiRequest', () => {
  it('sends JSON and returns the parsed response', async () => {
    fetchMock.mockResolvedValueOnce(reply(202, { challengeId: 'c1' }));

    const result = await apiRequest('/api/v1/auth/otp/request', {
      method: 'POST',
      body: { channel: 'sms', destination: '9876543210' },
    });

    expect(result).toEqual({ challengeId: 'c1' });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toMatch(/[^/]\/api\/v1\/auth\/otp\/request$/);
    expect(init.method).toBe('POST');
    expect(init.headers).toEqual({
      Accept: 'application/json',
      'Content-Type': 'application/json',
    });
    expect(JSON.parse(init.body as string)).toEqual({ channel: 'sms', destination: '9876543210' });
  });

  it('turns problem responses into an ApiError with the server code', async () => {
    fetchMock.mockResolvedValueOnce(
      reply(429, { code: 'too_many_requests', retryAfterSeconds: 30 }),
    );

    const error = await apiRequest('/api/v1/auth/otp/request', { method: 'POST' }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 429, code: 'too_many_requests', retryAfterSeconds: 30 });
  });

  it('falls back to the HTTP status when an error has no code', async () => {
    fetchMock.mockResolvedValueOnce(reply(500));

    await expect(apiRequest('/health/ready')).rejects.toMatchObject({
      status: 500,
      code: 'http_500',
    });
  });

  it('reports offline when the request cannot be sent', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('Network request failed'));

    const error = await apiRequest('/health/ready').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(NetworkError);
    expect(error).toMatchObject({ reason: 'offline' });
  });

  it('reports a timeout when the server does not answer in time', async () => {
    fetchMock.mockImplementationOnce(
      (_url, init) =>
        new Promise((_resolve, reject) => {
          init.signal?.addEventListener('abort', () => reject(new Error('Aborted')));
        }),
    );

    await expect(apiRequest('/health/ready', { timeoutMs: 10 })).rejects.toMatchObject({
      reason: 'timeout',
    });
  });

  describe('with auth', () => {
    const hooks = {
      getAccessToken: jest.fn<Promise<string | null>, []>(),
      refreshAccessToken: jest.fn<Promise<string | null>, []>(),
    };

    beforeEach(() => {
      hooks.getAccessToken.mockReset().mockResolvedValue('access-1');
      hooks.refreshAccessToken.mockReset().mockResolvedValue('access-2');
      configureApiAuth(hooks);
    });

    it('sends the access token', async () => {
      fetchMock.mockResolvedValueOnce(reply(200, { id: 'u1' }));

      await apiRequest('/api/v1/profile', { auth: true });

      expect(fetchMock.mock.calls[0][1].headers).toMatchObject({
        Authorization: 'Bearer access-1',
      });
    });

    it('refreshes once and retries after a 401', async () => {
      fetchMock.mockResolvedValueOnce(reply(401)).mockResolvedValueOnce(reply(200, { id: 'u1' }));

      await expect(apiRequest('/api/v1/profile', { auth: true })).resolves.toEqual({ id: 'u1' });
      expect(hooks.refreshAccessToken).toHaveBeenCalledTimes(1);
      expect(fetchMock.mock.calls[1][1].headers).toMatchObject({
        Authorization: 'Bearer access-2',
      });
    });

    it('reports an ended session when the refresh fails', async () => {
      hooks.refreshAccessToken.mockResolvedValue(null);
      fetchMock.mockResolvedValueOnce(reply(401));

      await expect(apiRequest('/api/v1/profile', { auth: true })).rejects.toMatchObject({
        status: 401,
        code: 'session_expired',
      });
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it('does not call the API without a session', async () => {
      hooks.getAccessToken.mockResolvedValue(null);

      await expect(apiRequest('/api/v1/profile', { auth: true })).rejects.toMatchObject({
        code: 'not_signed_in',
      });
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });
});
