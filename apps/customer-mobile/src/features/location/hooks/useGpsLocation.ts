import {
  LocationAccuracy,
  getCurrentPositionAsync,
  requestForegroundPermissionsAsync,
} from 'expo-location';
import { useCallback, useState } from 'react';

import { clearGpsLocation, setGpsLocation, useAppDispatch, useAppState } from '../../../state';

export type GpsStatus = 'idle' | 'requesting' | 'granted' | 'denied' | 'unavailable';

/**
 * Asks for the device's position only when the user chooses to (never on launch), and falls back
 * cleanly when it is denied or unavailable — the manual area picker keeps working either way
 * (spec §10, Phase 4: "manual location works without GPS permission").
 */
export function useGpsLocation() {
  const dispatch = useAppDispatch();
  const { location } = useAppState();
  const [status, setStatus] = useState<GpsStatus>('idle');

  const requestLocation = useCallback(async () => {
    setStatus('requesting');
    try {
      const permission = await requestForegroundPermissionsAsync();
      if (!permission.granted) {
        setStatus('denied');
        return;
      }

      const position = await getCurrentPositionAsync({ accuracy: LocationAccuracy.Balanced });
      dispatch(setGpsLocation(position.coords.latitude, position.coords.longitude));
      setStatus('granted');
    } catch {
      // No location services, a timeout, or an emulator with no fix: fall back silently.
      setStatus('unavailable');
    }
  }, [dispatch]);

  const clear = useCallback(() => {
    dispatch(clearGpsLocation());
    setStatus('idle');
  }, [dispatch]);

  return {
    status,
    requestLocation,
    clear,
    coordinates:
      location.source === 'gps' && location.latitude !== null && location.longitude !== null
        ? { latitude: location.latitude, longitude: location.longitude }
        : null,
  };
}
