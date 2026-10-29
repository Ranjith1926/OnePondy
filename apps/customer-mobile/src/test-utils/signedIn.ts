import { initialAppState, type AppState } from '../state';

/** App state for a customer who is signed in, for screens that only load data then. */
export const signedInState: AppState = {
  ...initialAppState,
  auth: {
    status: 'authenticated',
    user: { id: 'user-1', displayName: 'Asha', roles: ['customer'] },
  },
};

/** App state for a guest. */
export const guestState: AppState = {
  ...initialAppState,
  auth: { status: 'unauthenticated', user: null },
};
