import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import { useTheme } from '../design-system';
import { useUpdateRequired } from '../features/modules/hooks/useAppConfig';
import { UpdateRequiredScreen } from '../features/modules/screens/UpdateRequiredScreen';
import { createNavigationTheme } from './navigationTheme';
import { Navigation } from './RootNavigator';

export function AppNavigation() {
  const theme = useTheme();
  const { i18n } = useTranslation();
  const language = i18n.language;
  const updateRequired = useUpdateRequired();
  // A new theme object makes navigators re-evaluate screen options, so tab titles are
  // re-translated after a language change without resetting navigation state.
  const navigationTheme = useMemo(
    () => ({ ...createNavigationTheme(theme), language }),
    [theme, language],
  );
  // An unsupported version gets only the update screen (spec §8: minSupportedAppVersion).
  return updateRequired ? <UpdateRequiredScreen /> : <Navigation theme={navigationTheme} />;
}
