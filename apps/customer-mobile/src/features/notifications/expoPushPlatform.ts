import AsyncStorage from '@react-native-async-storage/async-storage';
import Constants from 'expo-constants';
import * as Notifications from 'expo-notifications';
import { AndroidImportance } from 'expo-notifications';
import { Platform } from 'react-native';

import { apiRequest } from '../../api/apiClient';
import type { PushApi, PushPlatform, PushStore, StoredRegistration } from './pushRegistration';

const registrationKey = 'onepondy.push.registration';
const askedKey = 'onepondy.push.askedForPermission';

function projectId(): string | null {
  const configured = Constants.expoConfig?.extra as { eas?: { projectId?: string } } | undefined;
  return configured?.eas?.projectId ?? Constants.easConfig?.projectId ?? null;
}

/** The real phone: Expo's notifications module. */
export const expoPushPlatform: PushPlatform = {
  os: Platform.OS === 'android' || Platform.OS === 'ios' ? Platform.OS : 'other',
  get projectId() {
    return projectId();
  },
  getPermission: async () => {
    const { status } = await Notifications.getPermissionsAsync();
    return status === 'granted' ? 'granted' : status === 'denied' ? 'denied' : 'undetermined';
  },
  requestPermission: async () => {
    const { status } = await Notifications.requestPermissionsAsync();
    return status === 'granted' ? 'granted' : 'denied';
  },
  ensureChannel: async () => {
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync('default', {
        name: 'Bookings',
        importance: AndroidImportance.HIGH,
      });
    }
  },
  getToken: async (id) => (await Notifications.getExpoPushTokenAsync({ projectId: id })).data,
};

export const pushApi: PushApi = {
  register: (token, platform) =>
    apiRequest<{ id: string }>('/api/v1/devices', {
      method: 'POST',
      body: { token, platform },
      auth: true,
    }),
  unregister: async (deviceId) => {
    await apiRequest<void>(`/api/v1/devices/${encodeURIComponent(deviceId)}`, {
      method: 'DELETE',
      auth: true,
    });
  },
};

export const pushStore: PushStore = {
  get: async () => {
    const raw = await AsyncStorage.getItem(registrationKey);
    return raw ? (JSON.parse(raw) as StoredRegistration) : null;
  },
  set: (value) => AsyncStorage.setItem(registrationKey, JSON.stringify(value)),
  clear: () => AsyncStorage.removeItem(registrationKey),
  hasAskedForPermission: async () => (await AsyncStorage.getItem(askedKey)) === '1',
  markAskedForPermission: () => AsyncStorage.setItem(askedKey, '1'),
};
