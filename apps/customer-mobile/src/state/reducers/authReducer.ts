import { ActionTypes } from '../actionTypes';
import type { AuthState } from '../appState';
import type { AppAction } from '../rootActions';

export function authReducer(state: AuthState, action: AppAction): AuthState {
  switch (action.type) {
    case ActionTypes.sessionRestored:
    case ActionTypes.signedIn:
      return { status: 'authenticated', user: action.payload.user };
    case ActionTypes.sessionCleared:
      return state.status === 'unauthenticated' && state.user === null
        ? state
        : { status: 'unauthenticated', user: null };
    case ActionTypes.userUpdated:
      return state.user === null ? state : { ...state, user: { ...state.user, ...action.payload } };
    default:
      return state;
  }
}
