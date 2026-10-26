export const ActionTypes = {
  sessionRestored: 'AUTH/SESSION_RESTORED',
  signedIn: 'AUTH/SIGNED_IN',
  sessionCleared: 'AUTH/SESSION_CLEARED',
  userUpdated: 'AUTH/USER_UPDATED',
  preferencesRestored: 'PREFERENCES/RESTORED',
  setLanguage: 'PREFERENCES/SET_LANGUAGE',
  setTheme: 'PREFERENCES/SET_THEME',
  completeOnboarding: 'PREFERENCES/COMPLETE_ONBOARDING',
  setSelectedLocation: 'LOCATION/SET_SELECTED',
  showBanner: 'UI/SHOW_BANNER',
  hideBanner: 'UI/HIDE_BANNER',
} as const;
