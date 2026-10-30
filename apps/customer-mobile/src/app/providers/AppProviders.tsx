import type { ReactNode } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { CartProvider } from '../../features/food/cart/CartContext';
import { AppStateProvider, type AppState } from '../../state';
import { AppThemeProvider } from './AppThemeProvider';
import { LanguageSync } from './LanguageSync';
import { PreferencesBootstrap } from './PreferencesBootstrap';
import { QueryProvider } from './QueryProvider';
import { SessionBootstrap } from './SessionBootstrap';
import { SessionSync } from './SessionSync';

interface AppProvidersProps {
  children: ReactNode;
  /** For tests: start from a specific app state. */
  initialState?: AppState;
}

export function AppProviders({ children, initialState }: AppProvidersProps) {
  return (
    <SafeAreaProvider>
      <QueryProvider>
        <AppStateProvider initialState={initialState}>
          <PreferencesBootstrap>
            <LanguageSync />
            <AppThemeProvider>
              <SessionBootstrap>
                <SessionSync />
                <CartProvider>{children}</CartProvider>
              </SessionBootstrap>
            </AppThemeProvider>
          </PreferencesBootstrap>
        </AppStateProvider>
      </QueryProvider>
    </SafeAreaProvider>
  );
}
