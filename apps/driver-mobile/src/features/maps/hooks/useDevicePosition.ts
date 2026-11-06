import * as Location from 'expo-location';
import { useCallback, useEffect, useState } from 'react';

export interface DevicePosition {
  latitude: number;
  longitude: number;
}

type Permission = 'unknown' | 'granted' | 'denied';

/**
 * Where this phone is, for drawing it on a map on this phone. The position is never sent anywhere. Nothing is asked until
 * <c>ask</c> is called, unless the driver has already allowed location, in which case the map just shows them. Reading stops when
 * the screen goes away.
 */
export function useDevicePosition(): {
  position: DevicePosition | null;
  permission: Permission;
  ask: () => void;
} {
  const [permission, setPermission] = useState<Permission>('unknown');
  const [position, setPosition] = useState<DevicePosition | null>(null);

  useEffect(() => {
    let cancelled = false;
    void Location.getForegroundPermissionsAsync()
      .then((result) => {
        if (!cancelled && result.status === 'granted') {
          setPermission('granted');
        }
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, []);

  const ask = useCallback(() => {
    void Location.requestForegroundPermissionsAsync()
      .then((result) => setPermission(result.status === 'granted' ? 'granted' : 'denied'))
      .catch(() => setPermission('denied'));
  }, []);

  useEffect(() => {
    if (permission !== 'granted') {
      return;
    }
    let cancelled = false;
    let subscription: Location.LocationSubscription | null = null;
    void Location.watchPositionAsync(
      { accuracy: Location.Accuracy.High, timeInterval: 15_000, distanceInterval: 25 },
      (reading) =>
        setPosition({ latitude: reading.coords.latitude, longitude: reading.coords.longitude }),
    )
      .then((started) => {
        if (cancelled) {
          started.remove();
        } else {
          subscription = started;
        }
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
      subscription?.remove();
    };
  }, [permission]);

  return { position, permission, ask };
}
