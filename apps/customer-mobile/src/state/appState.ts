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

/** Session summary only: tokens stay in SecureStore and profile details in the query cache. */
export interface SessionUser {
  id: string;
  displayName: string;
  roles: string[];
}

export interface AuthState {
  /** `unauthenticated` means a guest; public screens stay usable (spec §4 Guest access). */
  status: 'loading' | 'authenticated' | 'unauthenticated';
  user: SessionUser | null;
}

export type BannerKind = 'info' | 'success' | 'warning' | 'error';

/** i18n keys for messages that may appear in the global banner. */
export type BannerMessageKey =
  | 'banners.preferencesSaveFailed'
  | 'banners.sessionExpired'
  | 'banners.signedOut'
  | 'banners.accountDeleted';

export interface Banner {
  kind: BannerKind;
  messageKey: BannerMessageKey;
}

export interface UiState {
  banner: Banner | null;
}

// Never store passwords, tokens or payment data in app state (spec §6).
export interface AppState {
  auth: AuthState;
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
  auth: {
    status: 'loading',
    user: null,
  },
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
