import { createContext, useContext } from 'react';

export interface Connectivity {
  /** True while the phone has no connection, so anything still loading is waiting for it to come back. */
  offline: boolean;
  /** What to say about that, already in the customer's language: the design system holds no text of its own. */
  offlineMessage: string;
}

const ConnectivityContext = createContext<Connectivity>({ offline: false, offlineMessage: '' });

/** The app tells the design system whether it is offline, and in what words to say so. */
export const ConnectivityProvider = ConnectivityContext.Provider;

export function useConnectivity(): Connectivity {
  return useContext(ConnectivityContext);
}
