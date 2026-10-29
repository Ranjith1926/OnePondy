import { useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  ErrorState,
  InlineBanner,
  LoadingState,
  ModalSheet,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { formatMoney } from '../../payments/formatMoney';
import { PaymentCard } from '../../payments/components/PaymentCard';
import { ReceiptSection } from '../../payments/components/ReceiptSection';
import { bookingErrorMessageKey } from '../bookingErrors';
import { formatDateTime } from '../formatDateTime';
import { useBooking, useCancelBooking } from '../hooks/useBookings';
import { useRefetchOnFocus } from '../hooks/useRefetchOnFocus';

type Props = StaticScreenProps<{ id: string }>;

// A customer may also walk away from a booking that is waiting for payment (spec §7, decided 2026-10-29).
const cancellableStatuses = new Set(['pending', 'awaitingPayment', 'confirmed']);

export function BookingDetailScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const booking = useBooking(route.params.id);
  useRefetchOnFocus(booking.refetch);
  const cancelBooking = useCancelBooking(route.params.id);
  const [confirming, setConfirming] = useState(false);

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
    </View>
  );

  if (booking.isPending) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <View style={styles.centered}>
          <LoadingState accessibilityLabel={t('bookings.detail.loading')} />
        </View>
      </SafeAreaView>
    );
  }

  if (booking.isError) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <ErrorState
          title={t('bookings.detail.error.title')}
          message={t('bookings.detail.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void booking.refetch()}
          retrying={booking.isFetching}
        />
      </SafeAreaView>
    );
  }

  const data = booking.data;
  const canCancel = cancellableStatuses.has(data.status);

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      {header}
      <ScrollView contentContainerStyle={[styles.inset, styles.content]}>
        <OPText variant="title" accessibilityRole="header">
          {data.listingTitle}
        </OPText>
        <OPText tone="secondary">{data.businessName}</OPText>
        <OPText variant="label">{t(`bookings.status.${data.status}`)}</OPText>

        <View style={styles.section}>
          <OPText>{t('bookings.guestCount', { count: data.guestCount })}</OPText>
          {data.startAt ? <OPText>{formatDateTime(data.startAt)}</OPText> : null}
          <OPText variant="label">
            {formatMoney(data.priceSnapshot.totalAmount, data.priceSnapshot.currency)}
          </OPText>
        </View>

        {data.status === 'awaitingPayment' ? (
          <View style={styles.section}>
            <PaymentCard booking={data} />
          </View>
        ) : null}

        <ReceiptSection booking={data} />

        <View style={styles.section}>
          <OPText variant="label">{t('bookings.detail.cancellationPolicy')}</OPText>
          <OPText>
            {t('bookings.detail.freeUntil', {
              hours: data.cancellationPolicySnapshot.freeCancellationHours,
              percent: data.cancellationPolicySnapshot.refundPercentage,
            })}
          </OPText>
        </View>

        {data.cancellationReason ? (
          <View style={styles.section}>
            <OPText tone="secondary">
              {t('bookings.detail.reason', { reason: data.cancellationReason })}
            </OPText>
          </View>
        ) : null}

        <View style={styles.section}>
          <OPText variant="label">{t('bookings.detail.timeline')}</OPText>
          {data.history.map((entry, index) => (
            <View key={`${entry.newStatus}-${entry.createdAt}-${index}`} style={styles.timelineRow}>
              <OPText>{t(`bookings.status.${entry.newStatus}`)}</OPText>
              <OPText tone="secondary">{formatDateTime(entry.createdAt)}</OPText>
            </View>
          ))}
        </View>
      </ScrollView>

      {canCancel ? (
        <View style={[styles.inset, styles.footer]}>
          <OPButton
            label={t('bookings.cancel')}
            variant="secondary"
            onPress={() => setConfirming(true)}
            fullWidth
          />
        </View>
      ) : null}

      <ModalSheet
        visible={confirming}
        onClose={() => setConfirming(false)}
        title={t('bookings.cancelConfirm.title')}
        closeLabel={t('common.close')}
      >
        <View style={styles.confirmBody}>
          {cancelBooking.error ? (
            <InlineBanner kind="error" message={t(bookingErrorMessageKey(cancelBooking.error))} />
          ) : null}
          <OPText tone="secondary">
            {data.payment
              ? t('bookings.cancelConfirm.paidMessage', {
                  hours: data.cancellationPolicySnapshot.freeCancellationHours,
                  percent: data.cancellationPolicySnapshot.refundPercentage,
                })
              : t('bookings.cancelConfirm.message')}
          </OPText>
          <OPButton
            label={
              cancelBooking.isPending
                ? t('bookings.cancelling')
                : t('bookings.cancelConfirm.confirm')
            }
            variant="danger"
            loading={cancelBooking.isPending}
            onPress={() =>
              cancelBooking.mutate(
                { version: data.version },
                { onSuccess: () => setConfirming(false) },
              )
            }
            fullWidth
          />
          <OPButton
            label={t('bookings.cancelConfirm.keep')}
            variant="tertiary"
            onPress={() => setConfirming(false)}
            fullWidth
          />
        </View>
      </ModalSheet>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
    },
    centered: {
      flex: 1,
      alignItems: 'center',
      justifyContent: 'center',
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    content: {
      gap: theme.spacing.sm,
      paddingBottom: theme.spacing.xl,
    },
    section: {
      marginTop: theme.spacing.md,
      gap: theme.spacing.xxs,
    },
    timelineRow: {
      gap: theme.spacing.xxs,
      paddingVertical: theme.spacing.xxs,
    },
    footer: {
      paddingVertical: theme.spacing.sm,
      borderTopWidth: StyleSheet.hairlineWidth,
      borderTopColor: theme.colors.border,
    },
    confirmBody: {
      gap: theme.spacing.sm,
    },
  });
