import { ApiError, NetworkError } from '../../../api/apiErrors';
import { loadSession } from '../../../storage/sessionStorage';
import { authApi, type SessionTokens } from '../api/authApi';
import { SessionManager } from './sessionManager';

const user = { id: 'u1', displayName: 'Asha', roles: ['customer'] };

function inMinutes(minutes: number): string {
  return new Date(Date.now() + minutes * 60_000).toISOString();
}

function tokens(overrides: Partial<SessionTokens> = {}): SessionTokens {
  return {
    accessToken: 'access-1',
    accessTokenExpiresAt: inMinutes(15),
    refreshToken: 'refresh-1',
    refreshTokenExpiresAt: inMinutes(30 * 24 * 60),
    user,
    isNewUser: false,
    ...overrides,
  };
}

const refresh = jest.spyOn(authApi, 'refresh');

beforeEach(async () => {
  refresh.mockReset();
  await new SessionManager().clear();
});

describe('SessionManager', () => {
  it('saves a new session and restores it on the next launch', async () => {
    await new SessionManager().start(tokens());

    const restored = await new SessionManager().restore();

    expect(restored).toMatchObject({ refreshToken: 'refresh-1', user });
  });

  it('drops a saved session whose refresh token has expired', async () => {
    await new SessionManager().start(tokens({ refreshTokenExpiresAt: inMinutes(-1) }));

    await expect(new SessionManager().restore()).resolves.toBeNull();
    await expect(loadSession()).resolves.toBeNull();
  });

  it('returns the current access token while it is fresh', async () => {
    const manager = new SessionManager();
    await manager.start(tokens());

    await expect(manager.getAccessToken()).resolves.toBe('access-1');
    expect(refresh).not.toHaveBeenCalled();
  });

  it('refreshes a token that is about to expire, once for parallel callers', async () => {
    const manager = new SessionManager();
    await manager.start(tokens({ accessTokenExpiresAt: inMinutes(0.5) }));
    refresh.mockResolvedValue(tokens({ accessToken: 'access-2', refreshToken: 'refresh-2' }));

    const results = await Promise.all([
      manager.getAccessToken(),
      manager.getAccessToken(),
      manager.refresh(),
    ]);

    expect(results).toEqual(['access-2', 'access-2', 'access-2']);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(refresh).toHaveBeenCalledWith('refresh-1');
    expect((await loadSession())?.refreshToken).toBe('refresh-2');
  });

  it('ends the session and notifies when the server rejects the refresh token', async () => {
    const manager = new SessionManager();
    const expired = jest.fn();
    manager.onExpired(expired);
    await manager.start(tokens());
    refresh.mockRejectedValue(new ApiError(401, 'invalid_refresh_token'));

    await expect(manager.refresh()).resolves.toBeNull();

    expect(expired).toHaveBeenCalledTimes(1);
    expect(manager.currentUser).toBeNull();
    await expect(loadSession()).resolves.toBeNull();
  });

  it('keeps the session when a refresh fails for network reasons', async () => {
    const manager = new SessionManager();
    await manager.start(tokens());
    refresh.mockRejectedValue(new NetworkError('offline'));

    await expect(manager.refresh()).rejects.toMatchObject({ reason: 'offline' });

    expect(manager.currentUser).toEqual(user);
    await expect(loadSession()).resolves.not.toBeNull();
  });

  it('ignores a refresh failure that arrives after signing out', async () => {
    const manager = new SessionManager();
    const expired = jest.fn();
    manager.onExpired(expired);
    await manager.start(tokens());
    let rejectRefresh: (error: unknown) => void = () => undefined;
    refresh.mockReturnValue(
      new Promise((_resolve, reject) => {
        rejectRefresh = reject;
      }),
    );

    const pending = manager.refresh();
    await manager.clear();
    rejectRefresh(new ApiError(401, 'invalid_refresh_token'));

    await expect(pending).resolves.toBeNull();
    expect(expired).not.toHaveBeenCalled();
  });
});
