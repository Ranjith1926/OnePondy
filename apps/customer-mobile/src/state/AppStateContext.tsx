import { createContext, useContext, useReducer, type Dispatch, type ReactNode } from 'react';

import { initialAppState, type AppState } from './appState';
import type { AppAction } from './rootActions';
import { rootReducer } from './rootReducer';

// State and dispatch live in separate contexts so components that only dispatch
// do not re-render when state changes.
const AppStateContext = createContext<AppState | undefined>(undefined);
const AppDispatchContext = createContext<Dispatch<AppAction> | undefined>(undefined);

interface AppStateProviderProps {
  children: ReactNode;
  initialState?: AppState;
}

export function AppStateProvider({
  children,
  initialState = initialAppState,
}: AppStateProviderProps) {
  const [state, dispatch] = useReducer(rootReducer, initialState);
  return (
    <AppDispatchContext value={dispatch}>
      <AppStateContext value={state}>{children}</AppStateContext>
    </AppDispatchContext>
  );
}

export function useAppState(): AppState {
  const state = useContext(AppStateContext);
  if (state === undefined) {
    throw new Error('useAppState must be used inside AppStateProvider.');
  }
  return state;
}

export function useAppDispatch(): Dispatch<AppAction> {
  const dispatch = useContext(AppDispatchContext);
  if (dispatch === undefined) {
    throw new Error('useAppDispatch must be used inside AppStateProvider.');
  }
  return dispatch;
}
