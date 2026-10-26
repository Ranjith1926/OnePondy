import { ActionTypes } from './actionTypes';
import type { Banner, Language, PreferencesState, ThemePreference } from './appState';

export const preferencesRestored = (preferences: Partial<PreferencesState>) => ({
  type: ActionTypes.preferencesRestored,
  payload: preferences,
});

export const setLanguage = (language: Language) => ({
  type: ActionTypes.setLanguage,
  payload: { language },
});

export const setTheme = (theme: ThemePreference) => ({
  type: ActionTypes.setTheme,
  payload: { theme },
});

export const completeOnboarding = () => ({
  type: ActionTypes.completeOnboarding,
});

export const setSelectedLocation = (areaId: string) => ({
  type: ActionTypes.setSelectedLocation,
  payload: { areaId },
});

export const showBanner = (banner: Banner) => ({
  type: ActionTypes.showBanner,
  payload: banner,
});

export const hideBanner = () => ({
  type: ActionTypes.hideBanner,
});

export type AppAction =
  | ReturnType<typeof preferencesRestored>
  | ReturnType<typeof setLanguage>
  | ReturnType<typeof setTheme>
  | ReturnType<typeof completeOnboarding>
  | ReturnType<typeof setSelectedLocation>
  | ReturnType<typeof showBanner>
  | ReturnType<typeof hideBanner>;
