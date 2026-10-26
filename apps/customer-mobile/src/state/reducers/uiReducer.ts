import { ActionTypes } from '../actionTypes';
import type { UiState } from '../appState';
import type { AppAction } from '../rootActions';

export function uiReducer(state: UiState, action: AppAction): UiState {
  switch (action.type) {
    case ActionTypes.showBanner:
      return { ...state, banner: action.payload };
    case ActionTypes.hideBanner:
      return state.banner === null ? state : { ...state, banner: null };
    default:
      return state;
  }
}
