import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import { useTheme } from '../design-system';
import { createNavigationTheme } from './navigationTheme';
import { Navigation } from './RootNavigator';

export function AppNavigation() {
  const theme = useTheme();
  const { i18n } = useTranslation();
  const language = i18n.language;
  // A new theme object makes navigators re-evaluate screen options, so tab titles are
  // re-translated after a language change without resetting navigation state.
  const navigationTheme = useMemo(
    () => ({ ...createNavigationTheme(theme), language }),
    [theme, language],
  );
  return <Navigation theme={navigationTheme} />;
}
