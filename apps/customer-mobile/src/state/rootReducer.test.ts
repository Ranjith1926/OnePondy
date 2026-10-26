import { initialAppState, type AppState } from './appState';
import {
  completeOnboarding,
  hideBanner,
  preferencesRestored,
  sessionCleared,
  sessionRestored,
  setLanguage,
  setSelectedLocation,
  setTheme,
  showBanner,
  signedIn,
  userUpdated,
} from './rootActions';
import { rootReducer } from './rootReducer';

describe('rootReducer', () => {
  it('merges restored preferences over the defaults', () => {
    const next = rootReducer(initialAppState, preferencesRestored({ theme: 'dark' }));

    expect(next.preferences).toEqual({ language: 'en', theme: 'dark', onboardingCompleted: false });
  });

  it('sets language and theme', () => {
    const withLanguage = rootReducer(initialAppState, setLanguage('ta'));
    const withTheme = rootReducer(withLanguage, setTheme('light'));

    expect(withTheme.preferences.language).toBe('ta');
    expect(withTheme.preferences.theme).toBe('light');
  });

  it('completes onboarding', () => {
    expect(rootReducer(initialAppState, completeOnboarding()).preferences.onboardingCompleted).toBe(
      true,
    );
  });

  it('stores a manual area selection without coordinates', () => {
    const gpsState: AppState = {
      ...initialAppState,
      location: { selectedAreaId: null, latitude: 11.93, longitude: 79.83, source: 'gps' },
    };

    expect(rootReducer(gpsState, setSelectedLocation('auroville')).location).toEqual({
      selectedAreaId: 'auroville',
      latitude: null,
      longitude: null,
      source: 'manual',
    });
  });

  it('shows and hides the banner', () => {
    const shown = rootReducer(
      initialAppState,
      showBanner({ kind: 'error', messageKey: 'banners.preferencesSaveFailed' }),
    );
    expect(shown.ui.banner).toEqual({ kind: 'error', messageKey: 'banners.preferencesSaveFailed' });

    expect(rootReducer(shown, hideBanner()).ui.banner).toBeNull();
  });

  it('starts by restoring the session, then tracks sign-in, name changes and sign-out', () => {
    const user = { id: 'u1', displayName: '', roles: ['customer'] };
    expect(initialAppState.auth).toEqual({ status: 'loading', user: null });

    const restored = rootReducer(initialAppState, sessionRestored(user));
    expect(restored.auth).toEqual({ status: 'authenticated', user });

    const named = rootReducer(restored, userUpdated({ displayName: 'Asha' }));
    expect(named.auth.user).toEqual({ ...user, displayName: 'Asha' });

    const signedOut = rootReducer(named, sessionCleared());
    expect(signedOut.auth).toEqual({ status: 'unauthenticated', user: null });

    expect(rootReducer(signedOut, signedIn(user)).auth.status).toBe('authenticated');
  });

  it('ignores name changes and repeated sign-outs without a session', () => {
    const signedOut = rootReducer(initialAppState, sessionCleared());

    expect(rootReducer(signedOut, sessionCleared())).toBe(signedOut);
    expect(rootReducer(signedOut, userUpdated({ displayName: 'Asha' }))).toBe(signedOut);
  });

  it('returns the same state object when nothing changes', () => {
    expect(rootReducer(initialAppState, setTheme('system'))).toBe(initialAppState);
    expect(rootReducer(initialAppState, hideBanner())).toBe(initialAppState);
  });

  it('never mutates the previous state', () => {
    const frozen = Object.freeze({
      ...initialAppState,
      preferences: Object.freeze({ ...initialAppState.preferences }),
    });

    expect(() => rootReducer(frozen, setTheme('dark'))).not.toThrow();
    expect(frozen.preferences.theme).toBe('system');
  });
});
