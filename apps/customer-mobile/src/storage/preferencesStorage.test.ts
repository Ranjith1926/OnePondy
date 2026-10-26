import AsyncStorage from '@react-native-async-storage/async-storage';

import {
  loadStoredPreferences,
  parseStoredPreferences,
  saveStoredPreferences,
} from './preferencesStorage';

describe('preferencesStorage', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('round-trips saved preferences', async () => {
    await saveStoredPreferences({ language: 'en', theme: 'dark', onboardingCompleted: true });

    await expect(loadStoredPreferences()).resolves.toEqual({
      language: 'en',
      theme: 'dark',
      onboardingCompleted: true,
    });
  });

  it('returns nothing when no preferences are saved', async () => {
    await expect(loadStoredPreferences()).resolves.toEqual({});
  });

  it('ignores corrupted data', async () => {
    await AsyncStorage.setItem('onepondy.preferences.v1', '{not json');

    await expect(loadStoredPreferences()).resolves.toEqual({});
  });

  it('keeps only valid fields', () => {
    expect(
      parseStoredPreferences({
        language: 'de',
        theme: 'dark',
        onboardingCompleted: 'yes',
        extra: 1,
      }),
    ).toEqual({ theme: 'dark' });
    expect(parseStoredPreferences(null)).toEqual({});
    expect(parseStoredPreferences('dark')).toEqual({});
  });
});
