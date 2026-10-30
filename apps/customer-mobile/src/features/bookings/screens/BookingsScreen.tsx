import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  EmptyState,
  ErrorState,
  InlineBanner,
  ListItem,
  LoadingState,
  MainTabTemplate,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';
import { useOrders } from '../../food/hooks/useFood';
import { formatMoney } from '../../payments/formatMoney';
import { useBookings } from '../hooks/useBookings';
import { useRefetchOnFocus } from '../hooks/useRefetchOnFocus';

// Bookings need an account (spec §4 Guest access); the engine arrived in Phase 6. Food orders (Phase 8) are listed below them,
// so everything a customer has bought is in one place.
export function BookingsScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const bookings = useBookings();
  const orders = useOrders();
  useRefetchOnFocus(bookings.refetch, auth.status === 'authenticated');
  useRefetchOnFocus(orders.refetch, auth.status === 'authenticated');

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

  const hasOrders = orders.isSuccess && orders.data.length > 0;

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
      ) : hasOrders ? null : (
        <EmptyState
          icon="calendar-check"
          title={t('bookings.empty.title')}
          message={t('bookings.empty.message')}
          actionLabel={t('bookings.empty.action')}
          onAction={() => navigation.navigate('Listings', {})}
        />
      )}

      {orders.isError ? (
        <View style={styles.list}>
          <InlineBanner kind="error" message={t('food.orders.error')} />
        </View>
      ) : hasOrders ? (
        <View style={styles.list}>
          <OPText variant="label" accessibilityRole="header">
            {t('food.orders.title')}
          </OPText>
          {orders.data.map((order) => (
            <ListItem
              key={order.id}
              leadingIcon="utensils"
              title={order.restaurantName}
              subtitle={[
                t('food.orders.itemCount', { count: order.itemCount }),
                t(`food.order.status.${order.status}`),
                formatMoney(order.totalAmount, order.currency),
              ].join(' · ')}
              trailing={{ kind: 'chevron' }}
              onPress={() => navigation.navigate('OrderDetail', { id: order.id })}
            />
          ))}
        </View>
      ) : null}
    </MainTabTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    list: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
      paddingTop: theme.spacing.sm,
    },
  });
