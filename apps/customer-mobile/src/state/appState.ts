export type Language = 'en' | 'ta' | 'fr';
export type ThemePreference = 'light' | 'dark' | 'system';

export interface PreferencesState {
  language: Language;
  theme: ThemePreference;
  onboardingCompleted: boolean;
}

export interface LocationState {
  /** Manually chosen service area; GPS selection arrives with Phase 4. */
  selectedAreaId: string | null;
  latitude: number | null;
  longitude: number | null;
  source: 'gps' | 'manual' | null;
}

export type BannerKind = 'info' | 'success' | 'warning' | 'error';

/** i18n keys for messages that may appear in the global banner. */
export type BannerMessageKey = 'banners.preferencesSaveFailed';

export interface Banner {
  kind: BannerKind;
  messageKey: BannerMessageKey;
}

export interface UiState {
  banner: Banner | null;
}

// The auth slice is added in Phase 2 (spec §10). Never store passwords, tokens or payment data here.
export interface AppState {
  preferences: PreferencesState;
  location: LocationState;
  ui: UiState;
}

export const initialPreferences: PreferencesState = {
  language: 'en',
  theme: 'system',
  onboardingCompleted: false,
};

export const initialAppState: AppState = {
  preferences: initialPreferences,
  location: {
    selectedAreaId: null,
    latitude: null,
    longitude: null,
    source: null,
  },
  ui: {
    banner: null,
  },
};
