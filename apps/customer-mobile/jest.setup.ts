/* eslint-disable @typescript-eslint/no-require-imports -- jest.mock factories are hoisted and must use require() */
import '@testing-library/react-native';
import { timeoutManager } from '@tanstack/react-query';

import './src/i18n';

jest.mock('@react-native-async-storage/async-storage', () =>
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);

jest.mock(
  'react-native-safe-area-context',
  () => require('react-native-safe-area-context/jest/mock').default,
);

jest.mock('@react-native-community/netinfo', () =>
  require('@react-native-community/netinfo/jest/netinfo-mock.js'),
);

// SecureStore keeps values in memory; each test file starts empty.
jest.mock('expo-secure-store', () => {
  const values = new Map<string, string>();
  return {
    getItemAsync: jest.fn((key: string) => Promise.resolve(values.get(key) ?? null)),
    setItemAsync: jest.fn((key: string, value: string) => {
      values.set(key, value);
      return Promise.resolve();
    }),
    deleteItemAsync: jest.fn((key: string) => {
      values.delete(key);
      return Promise.resolve();
    }),
  };
});

// TanStack Query drops unused cache entries on five-minute timers. Unref'd timers still fire
// during a test, but no longer keep a finished Jest worker alive.
function unref<T>(timer: T): T {
  (timer as unknown as { unref?: () => void }).unref?.();
  return timer;
}
timeoutManager.setTimeoutProvider({
  setTimeout: (callback, delay) => unref(setTimeout(callback, delay)),
  clearTimeout: (id) => clearTimeout(id as Parameters<typeof clearTimeout>[0]),
  setInterval: (callback, delay) => unref(setInterval(callback, delay)),
  clearInterval: (id) => clearInterval(id as Parameters<typeof clearInterval>[0]),
});
