import AsyncStorage from '@react-native-async-storage/async-storage';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react-native';
import type { ReactNode } from 'react';

import { configRetryMs, useConfigUnreachable, useModuleStatus } from './useAppConfig';

const flags = {
  modules: { food: 'available', rides: 'available' },
  minSupportedAppVersion: '0.1.0',
};

function reply(config: unknown) {
  return Promise.resolve({
    ok: true,
    status: 200,
    text: () => Promise.resolve(JSON.stringify(config)),
  });
}

let client: QueryClient;

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe('module flags when the server is slow or missing', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(async () => {
    jest.useFakeTimers();
    client = new QueryClient();
    await AsyncStorage.clear();
  });

  afterEach(() => {
    client.clear();
    jest.useRealTimers();
    globalThis.fetch = originalFetch;
  });

  it('keeps asking until the server answers, so a slow first request does not leave everything on "Soon"', async () => {
    let calls = 0;
    globalThis.fetch = jest.fn(() => {
      calls += 1;
      return calls <= 5 ? Promise.reject(new TypeError('Network request failed')) : reply(flags);
    }) as unknown as typeof fetch;

    const { result } = await renderHook(
      () => ({ status: useModuleStatus(), unreachable: useConfigUnreachable() }),
      { wrapper },
    );
    expect(result.current.status.food).toBe('comingSoon');

    // The first request and its four retries (after 1, 2, 4 and 8 seconds) all fail; the next round is the periodic one.
    await act(async () => {
      await jest.advanceTimersByTimeAsync(16_000);
    });
    expect(result.current.unreachable).toBe(true);
    expect(result.current.status.food).toBe('comingSoon');

    await act(async () => {
      await jest.advanceTimersByTimeAsync(configRetryMs + 1_000);
    });
    expect(result.current.status.food).toBe('available');
    expect(result.current.unreachable).toBe(false);
  });

  it('uses the flags remembered from the last answer while the server cannot be reached', async () => {
    await AsyncStorage.setItem('onepondy.module-flags.v1', JSON.stringify(flags.modules));
    globalThis.fetch = jest.fn(() =>
      Promise.reject(new TypeError('Network request failed')),
    ) as unknown as typeof fetch;

    const { result } = await renderHook(
      () => ({ status: useModuleStatus(), unreachable: useConfigUnreachable() }),
      { wrapper },
    );
    await act(async () => {
      await jest.advanceTimersByTimeAsync(30_000);
    });

    expect(result.current.status.food).toBe('available');
    expect(result.current.status.rides).toBe('available');
    expect(result.current.unreachable).toBe(false);
  });

  it('remembers what the server says, for the next launch', async () => {
    globalThis.fetch = jest.fn(() => reply(flags)) as unknown as typeof fetch;

    await renderHook(() => useModuleStatus(), { wrapper });
    await act(async () => {
      await jest.advanceTimersByTimeAsync(1_000);
    });

    await expect(AsyncStorage.getItem('onepondy.module-flags.v1')).resolves.toBe(
      JSON.stringify(flags.modules),
    );
  });
});
