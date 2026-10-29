import { act, renderHook } from '@testing-library/react-native';

import { useSecondsLeft } from './useCountdown';

describe('useSecondsLeft', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  it('is null when there is no deadline', async () => {
    const { result } = await renderHook(() => useSecondsLeft(null));

    expect(result.current).toBeNull();
  });

  it('counts down each second and stops at zero', async () => {
    let clock = Date.parse('2026-10-29T10:00:00Z');
    const { result } = await renderHook(() => useSecondsLeft('2026-10-29T10:00:03Z', () => clock));
    expect(result.current).toBe(3);

    for (const expected of [2, 1, 0, 0]) {
      clock += 1_000;
      await act(async () => {
        jest.advanceTimersByTime(1_000);
      });
      expect(result.current).toBe(expected);
    }
  });
});
