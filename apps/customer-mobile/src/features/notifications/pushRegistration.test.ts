import {
  registerForPush,
  unregisterFromPush,
  type PushApi,
  type PushPlatform,
  type PushStore,
} from './pushRegistration';

function aPlatform(overrides: Partial<PushPlatform> = {}): jest.Mocked<PushPlatform> {
  return {
    os: 'android',
    projectId: 'project-1',
    getPermission: jest.fn().mockResolvedValue('granted'),
    requestPermission: jest.fn().mockResolvedValue('granted'),
    ensureChannel: jest.fn().mockResolvedValue(undefined),
    getToken: jest.fn().mockResolvedValue('ExponentPushToken[abc]'),
    ...overrides,
  } as jest.Mocked<PushPlatform>;
}

function anApi(): jest.Mocked<PushApi> {
  return {
    register: jest.fn().mockResolvedValue({ id: 'device-1' }),
    unregister: jest.fn().mockResolvedValue(undefined),
  };
}

function aStore(asked = false): jest.Mocked<PushStore> {
  return {
    get: jest.fn().mockResolvedValue(null),
    set: jest.fn().mockResolvedValue(undefined),
    clear: jest.fn().mockResolvedValue(undefined),
    hasAskedForPermission: jest.fn().mockResolvedValue(asked),
    markAskedForPermission: jest.fn().mockResolvedValue(undefined),
  };
}

describe('registerForPush', () => {
  it('registers the token and remembers the device when permission is already given', async () => {
    const platform = aPlatform();
    const api = anApi();
    const store = aStore();

    const result = await registerForPush({ platform, api, store, askIfNeeded: false });

    expect(result).toBe('registered');
    expect(platform.getToken).toHaveBeenCalledWith('project-1');
    expect(api.register).toHaveBeenCalledWith('ExponentPushToken[abc]', 'android');
    expect(store.set).toHaveBeenCalledWith({
      deviceId: 'device-1',
      token: 'ExponentPushToken[abc]',
    });
    expect(platform.requestPermission).not.toHaveBeenCalled();
  });

  it.each([
    ['there is no Expo project yet', { projectId: null }],
    ['the device is not a phone', { os: 'other' as const }],
  ])('is unavailable, without error, when %s', async (_name, overrides) => {
    const platform = aPlatform(overrides);
    const api = anApi();

    expect(await registerForPush({ platform, api, store: aStore(), askIfNeeded: true })).toBe(
      'unavailable',
    );
    expect(platform.getPermission).not.toHaveBeenCalled();
    expect(api.register).not.toHaveBeenCalled();
  });

  describe('when permission has not been decided', () => {
    it('does not ask when it was not told to', async () => {
      const platform = aPlatform({ getPermission: jest.fn().mockResolvedValue('undetermined') });
      const api = anApi();

      const result = await registerForPush({ platform, api, store: aStore(), askIfNeeded: false });

      expect(result).toBe('permission-needed');
      expect(platform.requestPermission).not.toHaveBeenCalled();
      expect(api.register).not.toHaveBeenCalled();
    });

    it('asks once, after making the channel, and registers when allowed', async () => {
      const platform = aPlatform({ getPermission: jest.fn().mockResolvedValue('undetermined') });
      const store = aStore();

      const result = await registerForPush({ platform, api: anApi(), store, askIfNeeded: true });

      expect(result).toBe('registered');
      expect(store.markAskedForPermission).toHaveBeenCalledTimes(1);
      expect(platform.ensureChannel.mock.invocationCallOrder[0]).toBeLessThan(
        platform.requestPermission.mock.invocationCallOrder[0],
      );
    });

    it('never asks a second time', async () => {
      const platform = aPlatform({ getPermission: jest.fn().mockResolvedValue('undetermined') });

      const result = await registerForPush({
        platform,
        api: anApi(),
        store: aStore(true),
        askIfNeeded: true,
      });

      expect(result).toBe('permission-needed');
      expect(platform.requestPermission).not.toHaveBeenCalled();
    });

    it('does not register when the customer says no', async () => {
      const platform = aPlatform({
        getPermission: jest.fn().mockResolvedValue('undetermined'),
        requestPermission: jest.fn().mockResolvedValue('denied'),
      });
      const api = anApi();

      const result = await registerForPush({ platform, api, store: aStore(), askIfNeeded: true });

      expect(result).toBe('permission-denied');
      expect(api.register).not.toHaveBeenCalled();
    });
  });

  it('does not register, or ask again, once permission has been refused', async () => {
    const platform = aPlatform({ getPermission: jest.fn().mockResolvedValue('denied') });
    const api = anApi();

    expect(await registerForPush({ platform, api, store: aStore(), askIfNeeded: true })).toBe(
      'permission-denied',
    );
    expect(platform.requestPermission).not.toHaveBeenCalled();
    expect(api.register).not.toHaveBeenCalled();
  });

  it.each([
    [
      'getting the token',
      (p: jest.Mocked<PushPlatform>) => p.getToken.mockRejectedValue(new Error('no play services')),
    ],
  ])('reports failed, not a crash, when %s throws', async (_name, breakIt) => {
    const platform = aPlatform();
    breakIt(platform);
    const store = aStore();

    expect(await registerForPush({ platform, api: anApi(), store, askIfNeeded: false })).toBe(
      'failed',
    );
    expect(store.set).not.toHaveBeenCalled();
  });

  it('reports failed when the server refuses the token', async () => {
    const api = anApi();
    api.register.mockRejectedValue(new Error('offline'));
    const store = aStore();

    expect(await registerForPush({ platform: aPlatform(), api, store, askIfNeeded: false })).toBe(
      'failed',
    );
    expect(store.set).not.toHaveBeenCalled();
  });
});

describe('unregisterFromPush', () => {
  it('tells the server and forgets the device', async () => {
    const api = anApi();
    const store = aStore();
    store.get.mockResolvedValue({ deviceId: 'device-1', token: 't' });

    await unregisterFromPush({ api, store });

    expect(api.unregister).toHaveBeenCalledWith('device-1');
    expect(store.clear).toHaveBeenCalled();
  });

  it('has nothing to tell the server when no device was registered', async () => {
    const api = anApi();
    const store = aStore();

    await unregisterFromPush({ api, store });

    expect(api.unregister).not.toHaveBeenCalled();
    expect(store.clear).toHaveBeenCalled();
  });

  it('still forgets the device here when the server cannot be reached', async () => {
    const api = anApi();
    api.unregister.mockRejectedValue(new Error('offline'));
    const store = aStore();
    store.get.mockResolvedValue({ deviceId: 'device-1', token: 't' });

    await expect(unregisterFromPush({ api, store })).resolves.toBeUndefined();
    expect(store.clear).toHaveBeenCalled();
  });
});
