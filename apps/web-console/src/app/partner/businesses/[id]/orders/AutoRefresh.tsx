'use client';

import { useRouter } from 'next/navigation';
import { useEffect } from 'react';

/**
 * Re-reads the page every few seconds while it is open and visible, so a new order shows up without the restaurant reloading. The
 * refresh keeps what they are typing (a half-written reason stays), and it pauses while the tab is hidden.
 */
export function AutoRefresh({ seconds }: { seconds: number }) {
  const router = useRouter();

  useEffect(() => {
    const timer = setInterval(() => {
      if (document.visibilityState === 'visible') {
        router.refresh();
      }
    }, seconds * 1000);
    return () => clearInterval(timer);
  }, [router, seconds]);

  return null;
}
