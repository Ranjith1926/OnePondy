import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';

import { EmptyState, MainTabTemplate } from '../../../design-system';

// Bookings arrive with the booking engine in Phase 6; until then the tab explains that honestly.
export function BookingsScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  return (
    <MainTabTemplate title={t('bookings.title')}>
      <EmptyState
        icon="calendar-check"
        title={t('bookings.empty.title')}
        message={t('bookings.empty.message')}
        actionLabel={t('bookings.empty.action')}
        onAction={() => navigation.navigate('Main', { screen: 'Explore' })}
      />
    </MainTabTemplate>
  );
}
