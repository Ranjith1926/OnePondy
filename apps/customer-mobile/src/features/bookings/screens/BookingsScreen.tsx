import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';

import { EmptyState, MainTabTemplate } from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';

// Bookings need an account (spec §4 Guest access) and arrive with the booking engine in Phase 6;
// until then the tab explains that honestly.
export function BookingsScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const { auth } = useAppState();
  return (
    <MainTabTemplate title={t('bookings.title')}>
      {auth.status === 'authenticated' ? (
        <EmptyState
          icon="calendar-check"
          title={t('bookings.empty.title')}
          message={t('bookings.empty.message')}
          actionLabel={t('bookings.empty.action')}
          onAction={() => navigation.navigate('Main', { screen: 'Explore' })}
        />
      ) : (
        <SignInPrompt
          icon="calendar-check"
          title={t('bookings.guest.title')}
          message={t('bookings.guest.message')}
        />
      )}
    </MainTabTemplate>
  );
}
