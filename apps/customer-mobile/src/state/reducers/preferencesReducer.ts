import { ActionTypes } from '../actionTypes';
import type { PreferencesState } from '../appState';
import type { AppAction } from '../rootActions';

export function preferencesReducer(state: PreferencesState, action: AppAction): PreferencesState {
  switch (action.type) {
    case ActionTypes.preferencesRestored:
      return { ...state, ...action.payload };
    case ActionTypes.setLanguage:
      return state.language === action.payload.language
        ? state
        : { ...state, language: action.payload.language };
    case ActionTypes.setTheme:
      return state.theme === action.payload.theme
        ? state
        : { ...state, theme: action.payload.theme };
    case ActionTypes.completeOnboarding:
      return state.onboardingCompleted ? state : { ...state, onboardingCompleted: true };
    default:
      return state;
  }
}
