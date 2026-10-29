import { useEffect, useState } from 'react';

/** Whole seconds left until `deadline` (an ISO time), counting down once a second. Null when there is no deadline. */
export function useSecondsLeft(
  deadline: string | null,
  now: () => number = Date.now,
): number | null {
  // The tick only makes this re-render each second; the value itself is worked out from the clock while rendering.
  const [, setTick] = useState(0);

  useEffect(() => {
    if (deadline === null) {
      return undefined;
    }

    const timer = setInterval(() => setTick((tick) => tick + 1), 1_000);
    return () => clearInterval(timer);
  }, [deadline]);

  return deadline === null ? null : Math.max(0, Math.ceil((Date.parse(deadline) - now()) / 1_000));
}
