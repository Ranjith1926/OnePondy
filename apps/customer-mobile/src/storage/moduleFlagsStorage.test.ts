import AsyncStorage from '@react-native-async-storage/async-storage';

import {
  loadStoredModuleFlags,
  parseStoredModuleFlags,
  saveStoredModuleFlags,
} from './moduleFlagsStorage';

describe('moduleFlagsStorage', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('round-trips the flags the server sent', async () => {
    await saveStoredModuleFlags({ food: 'available', rides: 'comingSoon' });

    await expect(loadStoredModuleFlags()).resolves.toEqual({
      food: 'available',
      rides: 'comingSoon',
    });
  });

  it('returns nothing when no flags are saved', async () => {
    await expect(loadStoredModuleFlags()).resolves.toBeUndefined();
  });

  it('ignores corrupted data', async () => {
    await AsyncStorage.setItem('onepondy.module-flags.v1', '{not json');

    await expect(loadStoredModuleFlags()).resolves.toBeUndefined();
  });

  it('keeps only string flags', () => {
    expect(parseStoredModuleFlags({ food: 'available', rides: 3, stays: null })).toEqual({
      food: 'available',
    });
    expect(parseStoredModuleFlags({ rides: 3 })).toBeUndefined();
    expect(parseStoredModuleFlags(['available'])).toBeUndefined();
    expect(parseStoredModuleFlags(null)).toBeUndefined();
    expect(parseStoredModuleFlags('available')).toBeUndefined();
  });
});
