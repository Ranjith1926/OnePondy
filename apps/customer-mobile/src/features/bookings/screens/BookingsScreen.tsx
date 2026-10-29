import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  EmptyState,
  ErrorState,
  ListItem,
  LoadingState,
  MainTabTemplate,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';
import { useBookings } from '../hooks/useBookings';
import { useRefetchOnFocus } from '../hooks/useRefetchOnFocus';

// Bookings need an account (spec §4 Guest access); the engine arrived in Phase 6.
export function BookingsScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const bookings = useBookings();
  useRefetchOnFocus(bookings.refetch, auth.status === 'authenticated');

  if (auth.status !== 'authenticated') {
    return (
      <MainTabTemplate title={t('bookings.title')}>
        <SignInPrompt
          icon="calendar-check"
          title={t('bookings.guest.title')}
          message={t('bookings.guest.message')}
        />
      </MainTabTemplate>
    );
  }

  return (
    <MainTabTemplate title={t('bookings.title')}>
      {bookings.isPending ? (
        <View style={styles.list}>
          <LoadingState accessibilityLabel={t('bookings.loading')} />
        </View>
      ) : bookings.isError ? (
        <ErrorState
          title={t('bookings.error.title')}
          message={t('bookings.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void bookings.refetch()}
          retrying={bookings.isFetching}
        />
      ) : bookings.data.length > 0 ? (
        <View style={styles.list}>
          {bookings.data.map((booking) => (
            <ListItem
              key={booking.id}
              leadingIcon="calendar-check"
              title={booking.listingTitle}
              subtitle={`${booking.businessName} · ${t(`bookings.status.${booking.status}`)}`}
              trailing={{ kind: 'chevron' }}
              onPress={() => navigation.navigate('BookingDetail', { id: booking.id })}
            />
          ))}
        </View>
      ) : (
        <EmptyState
          icon="calendar-check"
          title={t('bookings.empty.title')}
          message={t('bookings.empty.message')}
          actionLabel={t('bookings.empty.action')}
          onAction={() => navigation.navigate('Listings', {})}
        />
      )}
    </MainTabTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    list: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
    },
  });
