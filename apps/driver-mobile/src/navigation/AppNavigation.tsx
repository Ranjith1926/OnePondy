import { useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { View } from 'react-native';

import { ErrorState, LoadingState, useTheme } from '../design-system';
import { useDriverProfile } from '../features/driver/hooks/useDriver';
import { isNetworkError } from '../api/apiErrors';
import { useSession } from '../session/SessionProvider';
import { createNavigationTheme } from './navigationTheme';
import { Navigation } from './RootNavigator';
import './types';

/**
 * A signed-in driver's screens depend on whether they have applied, so nothing shows until their profile has loaded. If it
 * cannot load there is a retry, and the navigator is never built with no screens to show.
 */
function ProfileGate({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const profile = useDriverProfile();

  if (profile.data) {
    return children;
  }
  return (
    <View style={{ flex: 1, justifyContent: 'center', backgroundColor: theme.colors.background }}>
      {profile.isError ? (
        <ErrorState
          title={t('driver.loadErrorTitle')}
          message={t('driver.loadErrorMessage')}
          retryLabel={t('common.retry')}
          onRetry={() => void profile.refetch()}
          kind={isNetworkError(profile.error) ? 'offline' : 'error'}
          retrying={profile.isFetching}
        />
      ) : (
        <LoadingState accessibilityLabel={t('driver.loading')} />
      )}
    </View>
  );
}

export function AppNavigation() {
  const theme = useTheme();
  const { status } = useSession();
  const navigationTheme = useMemo(() => createNavigationTheme(theme), [theme]);
  const navigation = <Navigation theme={navigationTheme} />;

  return status === 'signedIn' ? <ProfileGate>{navigation}</ProfileGate> : navigation;
}
