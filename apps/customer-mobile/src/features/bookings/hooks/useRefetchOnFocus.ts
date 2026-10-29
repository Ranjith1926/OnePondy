import { useFocusEffect } from '@react-navigation/native';
import { useCallback } from 'react';

/**
 * Re-reads a query each time its screen comes into focus. Tab screens stay mounted, so without this a booking that changed
 * elsewhere (the partner accepting it, a payment confirmed by the server) would only show after the app was reloaded. A read
 * already in flight is left alone.
 */
export function useRefetchOnFocus(
  refetch: (options: { cancelRefetch: boolean }) => unknown,
  enabled = true,
): void {
  useFocusEffect(
    useCallback(() => {
      if (enabled) {
        void refetch({ cancelRefetch: false });
      }
    }, [refetch, enabled]),
  );
}
