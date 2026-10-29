import { useNavigationContainerRef, type StaticParamList } from '@react-navigation/native';
import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import { useTheme } from '../design-system';
import { useUpdateRequired } from '../features/modules/hooks/useAppConfig';
import { UpdateRequiredScreen } from '../features/modules/screens/UpdateRequiredScreen';
import { createNavigationTheme } from './navigationTheme';
import {
  launchLink,
  listenForTaps,
  showNotificationsWhileOpen,
} from '../features/notifications/notificationHandling';
import { useRegisterPushWhenSignedIn } from '../features/notifications/hooks/usePushSetup';
import { useAppState } from '../state';
import { Navigation, type RootStack } from './RootNavigator';

type RootParamList = StaticParamList<typeof RootStack>;

export function AppNavigation() {
  const theme = useTheme();
  const { i18n } = useTranslation();
  const language = i18n.language;
  const updateRequired = useUpdateRequired();
  const signedIn = useAppState().auth.status === 'authenticated';
  const navigationRef = useNavigationContainerRef<RootParamList>();
  useRegisterPushWhenSignedIn();

  // A notification opens its booking, but only for a signed-in customer; the booking itself still loads through the
  // signed-in API, so a link that is not theirs shows nothing.
  useEffect(() => {
    showNotificationsWhileOpen();
  }, []);
  useEffect(
    () =>
      listenForTaps((link) => {
        if (signedIn && navigationRef.isReady()) {
          navigationRef.navigate('BookingDetail', { id: link.bookingId });
        }
      }),
    [signedIn, navigationRef],
  );
  const openLaunchNotification = () => {
    const link = launchLink();
    if (link && signedIn) {
      navigationRef.navigate('BookingDetail', { id: link.bookingId });
    }
  };
  // A new theme object makes navigators re-evaluate screen options, so tab titles are
  // re-translated after a language change without resetting navigation state.
  const navigationTheme = useMemo(
    () => ({ ...createNavigationTheme(theme), language }),
    [theme, language],
  );
  // An unsupported version gets only the update screen (spec §8: minSupportedAppVersion).
  return updateRequired ? (
    <UpdateRequiredScreen />
  ) : (
    <Navigation ref={navigationRef} theme={navigationTheme} onReady={openLaunchNotification} />
  );
}
