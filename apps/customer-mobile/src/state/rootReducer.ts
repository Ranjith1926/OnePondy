import type { AppState } from './appState';
import { locationReducer } from './reducers/locationReducer';
import { preferencesReducer } from './reducers/preferencesReducer';
import { uiReducer } from './reducers/uiReducer';
import type { AppAction } from './rootActions';

/** Pure and synchronous: no API calls, storage, timers or navigation (spec §6). */
export function rootReducer(state: AppState, action: AppAction): AppState {
  const preferences = preferencesReducer(state.preferences, action);
  const location = locationReducer(state.location, action);
  const ui = uiReducer(state.ui, action);

  // Keep the same object when nothing changed so consumers do not re-render.
  if (preferences === state.preferences && location === state.location && ui === state.ui) {
    return state;
  }
  return { preferences, location, ui };
}
