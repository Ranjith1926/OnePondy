import AsyncStorage from '@react-native-async-storage/async-storage';

import type { Language, PreferencesState, ThemePreference } from '../state';

// Non-sensitive preferences only; tokens and session material go to SecureStore (spec §6).
const STORAGE_KEY = 'onepondy.preferences.v1';

const languages: readonly Language[] = ['en', 'ta', 'fr'];
const themes: readonly ThemePreference[] = ['light', 'dark', 'system'];

function isOneOf<T extends string>(options: readonly T[], value: unknown): value is T {
  return typeof value === 'string' && (options as readonly string[]).includes(value);
}

/** Keeps only valid fields, so corrupted or outdated data falls back to defaults. */
export function parseStoredPreferences(value: unknown): Partial<PreferencesState> {
  if (typeof value !== 'object' || value === null) {
    return {};
  }
  const record = value as Record<string, unknown>;
  const preferences: Partial<PreferencesState> = {};
  if (isOneOf(languages, record.language)) preferences.language = record.language;
  if (isOneOf(themes, record.theme)) preferences.theme = record.theme;
  if (typeof record.onboardingCompleted === 'boolean') {
    preferences.onboardingCompleted = record.onboardingCompleted;
  }
  return preferences;
}

export async function loadStoredPreferences(): Promise<Partial<PreferencesState>> {
  const raw = await AsyncStorage.getItem(STORAGE_KEY);
  if (raw === null) {
    return {};
  }
  try {
    return parseStoredPreferences(JSON.parse(raw));
  } catch {
    return {};
  }
}

export async function saveStoredPreferences(preferences: PreferencesState): Promise<void> {
  await AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(preferences));
}
