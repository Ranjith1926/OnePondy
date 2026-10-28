import { useEffect, useState } from 'react';

/** Delays updating the returned value until it stops changing for `delayMs`, to avoid a server
 * request on every keystroke while typing a search query. */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
