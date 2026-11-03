import { act, renderHook } from '@testing-library/react-native';

import { useRideLive } from './useRideLive';

type Handler = (...args: unknown[]) => void;

const mockHandlers: Record<string, Handler> = {};
const mockStart = jest.fn();
const mockStop = jest.fn();

jest.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl = jest.fn(() => this);
    withAutomaticReconnect = jest.fn(() => this);
    configureLogging = jest.fn(() => this);
    build = jest.fn(() => ({
      on: (name: string, handler: Handler) => {
        mockHandlers[name] = handler;
      },
      onreconnecting: (handler: Handler) => {
        mockHandlers.reconnecting = handler;
      },
      onreconnected: (handler: Handler) => {
        mockHandlers.reconnected = handler;
      },
      onclose: (handler: Handler) => {
        mockHandlers.close = handler;
      },
      start: mockStart,
      stop: mockStop,
    }));
  }
  return { HubConnectionBuilder, LogLevel: { None: 6 } };
});
jest.mock('../../../api/apiClient', () => ({ getApiAccessToken: jest.fn() }));

beforeEach(() => {
  for (const key of Object.keys(mockHandlers)) {
    delete mockHandlers[key];
  }
  mockStart.mockReset();
  mockStop.mockReset();
  mockStop.mockResolvedValue(undefined);
});

afterEach(() => jest.restoreAllMocks());

describe('useRideLive', () => {
  it('connects, says so, and refetches once because a message sent before connecting is gone', async () => {
    mockStart.mockResolvedValue(undefined);
    const onChange = jest.fn();

    const { result } = await renderHook(() => useRideLive(true, onChange));
    await act(async () => {});

    expect(result.current.connected).toBe(true);
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('refetches when the hub says a ride changed', async () => {
    mockStart.mockResolvedValue(undefined);
    const onChange = jest.fn();
    await renderHook(() => useRideLive(true, onChange));
    await act(async () => {});
    onChange.mockClear();

    await act(async () => mockHandlers.rideChanged());

    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('is not connected while reconnecting, and refetches when it is back', async () => {
    mockStart.mockResolvedValue(undefined);
    const onChange = jest.fn();
    const { result } = await renderHook(() => useRideLive(true, onChange));
    await act(async () => {});
    onChange.mockClear();

    await act(async () => mockHandlers.reconnecting());
    expect(result.current.connected).toBe(false);

    await act(async () => mockHandlers.reconnected());
    expect(result.current.connected).toBe(true);
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('keeps trying after the connection gives up, and after a start that fails', async () => {
    // Only the 30-second retry timer is captured, so the test can run it at once; everything else keeps its real timer.
    const retries: (() => void)[] = [];
    const realSetTimeout = globalThis.setTimeout;
    jest.spyOn(globalThis, 'setTimeout').mockImplementation(((
      fn: () => void,
      ms?: number,
      ...rest: unknown[]
    ) => {
      if (ms === 30_000) {
        retries.push(fn);
        return 0;
      }
      return realSetTimeout(fn, ms, ...rest);
    }) as never);
    mockStart.mockRejectedValueOnce(new Error('offline')).mockResolvedValue(undefined);

    const { result } = await renderHook(() => useRideLive(true, jest.fn()));
    await act(async () => {});
    expect(result.current.connected).toBe(false);
    expect(retries).toHaveLength(1);

    await act(async () => retries.shift()?.());
    expect(result.current.connected).toBe(true);

    await act(async () => mockHandlers.close());
    expect(result.current.connected).toBe(false);
    expect(retries).toHaveLength(1);
    await act(async () => retries.shift()?.());
    expect(mockStart).toHaveBeenCalledTimes(3);
    expect(result.current.connected).toBe(true);
  });

  it('does not connect when switched off, and disconnects when unmounted', async () => {
    mockStart.mockResolvedValue(undefined);
    const off = await renderHook(() => useRideLive(false, jest.fn()));
    expect(mockStart).not.toHaveBeenCalled();
    expect(off.result.current.connected).toBe(false);

    const on = await renderHook(() => useRideLive(true, jest.fn()));
    await act(async () => {});
    await on.unmount();
    expect(mockStop).toHaveBeenCalled();
  });
});
