import { ActionTypes } from './actionTypes';
import type { Banner, Language, PreferencesState, SessionUser, ThemePreference } from './appState';

export const sessionRestored = (user: SessionUser) => ({
  type: ActionTypes.sessionRestored,
  payload: { user },
});

export const signedIn = (user: SessionUser) => ({
  type: ActionTypes.signedIn,
  payload: { user },
});

export const sessionCleared = () => ({
  type: ActionTypes.sessionCleared,
});

export const userUpdated = (changes: Pick<SessionUser, 'displayName'>) => ({
  type: ActionTypes.userUpdated,
  payload: changes,
});

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

/** GPS coordinates are in-memory only; never persisted (spec §12). */
export const setGpsLocation = (latitude: number, longitude: number) => ({
  type: ActionTypes.setGpsLocation,
  payload: { latitude, longitude },
});

export const clearGpsLocation = () => ({
  type: ActionTypes.clearGpsLocation,
});

export const showBanner = (banner: Banner) => ({
  type: ActionTypes.showBanner,
  payload: banner,
});

export const hideBanner = () => ({
  type: ActionTypes.hideBanner,
});

export type AppAction =
  | ReturnType<typeof sessionRestored>
  | ReturnType<typeof signedIn>
  | ReturnType<typeof sessionCleared>
  | ReturnType<typeof userUpdated>
  | ReturnType<typeof preferencesRestored>
  | ReturnType<typeof setLanguage>
  | ReturnType<typeof setTheme>
  | ReturnType<typeof completeOnboarding>
  | ReturnType<typeof setSelectedLocation>
  | ReturnType<typeof setGpsLocation>
  | ReturnType<typeof clearGpsLocation>
  | ReturnType<typeof showBanner>
  | ReturnType<typeof hideBanner>;
