import type { AppState } from './appState';
import { authReducer } from './reducers/authReducer';
import { locationReducer } from './reducers/locationReducer';
import { preferencesReducer } from './reducers/preferencesReducer';
import { uiReducer } from './reducers/uiReducer';
import type { AppAction } from './rootActions';

/** Pure and synchronous: no API calls, storage, timers or navigation (spec §6). */
export function rootReducer(state: AppState, action: AppAction): AppState {
  const auth = authReducer(state.auth, action);
  const preferences = preferencesReducer(state.preferences, action);
  const location = locationReducer(state.location, action);
  const ui = uiReducer(state.ui, action);

  // Keep the same object when nothing changed so consumers do not re-render.
  if (
    auth === state.auth &&
    preferences === state.preferences &&
    location === state.location &&
    ui === state.ui
  ) {
    return state;
  }
  return { auth, preferences, location, ui };
}
