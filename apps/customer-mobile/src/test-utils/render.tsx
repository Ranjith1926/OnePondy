import { render } from '@testing-library/react-native';
import type { ReactElement, ReactNode } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { ThemeProvider, type ColorScheme } from '../design-system';
import type { CartState } from '../features/food/cart/cart';
import { CartProvider } from '../features/food/cart/CartContext';
import { AppStateProvider, initialAppState, type AppState } from '../state';

interface Options {
  scheme?: ColorScheme;
  initialState?: AppState;
  /** Dishes already in the food cart. */
  initialCart?: CartState;
}

/** Renders UI with the providers components expect: safe area, theme, app state and the food cart. */
export function renderWithProviders(
  ui: ReactElement,
  { scheme = 'light', initialState = initialAppState, initialCart }: Options = {},
) {
  function TestProviders({ children }: { children: ReactNode }) {
    return (
      <SafeAreaProvider>
        <AppStateProvider initialState={initialState}>
          <CartProvider initialCart={initialCart}>
            <ThemeProvider scheme={scheme}>{children}</ThemeProvider>
          </CartProvider>
        </AppStateProvider>
      </SafeAreaProvider>
    );
  }
  return render(ui, { wrapper: TestProviders });
}
