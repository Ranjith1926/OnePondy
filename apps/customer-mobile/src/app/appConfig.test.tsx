import AsyncStorage from '@react-native-async-storage/async-storage';
import { render, screen } from '@testing-library/react-native';

import App from './App';

// The test environment has no app.json version (Constants.expoConfig is empty there), so the
// minimum-version check is given a version to compare against.
jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { version: '0.1.0', android: { package: 'com.onepondy.customer' } } },
}));

function serveConfig(config: unknown) {
  globalThis.fetch = jest.fn((url: string) => {
    const found = url.endsWith('/api/v1/config');
    return Promise.resolve({
      ok: found,
      status: found ? 200 : 404,
      text: () => Promise.resolve(found ? JSON.stringify(config) : ''),
    });
  }) as unknown as typeof fetch;
}

async function launch() {
  await AsyncStorage.setItem(
    'onepondy.preferences.v1',
    JSON.stringify({ language: 'en', theme: 'system', onboardingCompleted: true }),
  );
  await render(<App />);
}

describe('app configuration from the server', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  afterAll(() => {
    globalThis.fetch = originalFetch;
  });

  it('hides modules the console has hidden, without an app release', async () => {
    serveConfig({
      modules: {
        food: 'hidden',
        tripPlanner: 'hidden',
        notifications: 'hidden',
        rides: 'available',
      },
      minSupportedAppVersion: '0.1.0',
    });
    await launch();

    expect(await screen.findByRole('button', { name: 'Rides, Soon' })).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Food, Soon' })).not.toBeOnTheScreen();
    expect(screen.queryByText('Plan my Pondy trip')).not.toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Notifications' })).not.toBeOnTheScreen();
  });

  it('asks people on an unsupported version to update', async () => {
    serveConfig({ modules: {}, minSupportedAppVersion: '99.0.0' });
    await launch();

    expect(await screen.findByRole('header', { name: 'Update OnePondy' })).toBeOnTheScreen();
    expect(
      screen.queryByRole('button', { name: 'Where do you want to go?' }),
    ).not.toBeOnTheScreen();
  });

  it('keeps working with safe defaults when the server cannot be reached', async () => {
    globalThis.fetch = jest.fn(() =>
      Promise.reject(new TypeError('Network request failed')),
    ) as unknown as typeof fetch;
    await launch();

    expect(await screen.findByRole('button', { name: 'Food, Soon' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Notifications' })).toBeOnTheScreen();
  });
});
