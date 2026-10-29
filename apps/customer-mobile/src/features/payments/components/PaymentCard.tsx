import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  InlineBanner,
  OPButton,
  OPText,
  useThemedStyles,
  type BannerKind,
  type Theme,
} from '../../../design-system';
import type { BookingDetail } from '../../bookings/api/bookingsApi';
import { bookingErrorMessageKey } from '../../bookings/bookingErrors';
import { useSecondsLeft } from '../hooks/useCountdown';
import { usePayBooking } from '../hooks/usePayBooking';
import { formatCountdown, formatMoney } from '../formatMoney';
import type { PayOutcome } from '../paymentFlow';

interface Props {
  booking: BookingDetail;
}

const outcomeBanners: Record<Exclude<PayOutcome['kind'], 'error'>, BannerKind> = {
  confirmed: 'success',
  processing: 'info',
  cancelled: 'warning',
  failed: 'error',
  lapsed: 'warning',
};

/**
 * Shown while the partner has accepted a booking and the customer still has to pay. The button opens the gateway's
 * checkout; whatever it reports, the booking is then re-read from the server, which alone decides if it is paid.
 */
export function PaymentCard({ booking }: Props) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const pay = usePayBooking(booking.id, booking.listingTitle);
  const secondsLeft = useSecondsLeft(booking.expiresAt);
  const timeIsUp = secondsLeft !== null && secondsLeft <= 0;
  const outcome = pay.data;

  return (
    <View style={styles.card}>
      <OPText variant="title" accessibilityRole="header">
        {t('payments.pay.title')}
      </OPText>
      <OPText tone="secondary">{t('payments.pay.accepted')}</OPText>
      <OPText variant="label">
        {formatMoney(booking.priceSnapshot.totalAmount, booking.priceSnapshot.currency)}
      </OPText>

      {secondsLeft !== null ? (
        <OPText tone={timeIsUp ? 'error' : 'secondary'} accessibilityLiveRegion="polite">
          {timeIsUp
            ? t('payments.pay.timeUp')
            : t('payments.pay.timeLeft', { time: formatCountdown(secondsLeft) })}
        </OPText>
      ) : null}

      {pay.isError ? <InlineBanner kind="error" message={t('payments.outcome.error')} /> : null}
      {outcome?.kind === 'error' ? (
        <InlineBanner kind="error" message={t(bookingErrorMessageKey(outcome.error))} />
      ) : null}
      {outcome && outcome.kind !== 'error' && outcome.kind !== 'confirmed' ? (
        <InlineBanner
          kind={outcomeBanners[outcome.kind]}
          message={t(`payments.outcome.${outcome.kind}`)}
        />
      ) : null}

      <OPButton
        label={
          pay.isPending
            ? t('payments.pay.opening')
            : t('payments.pay.button', {
                amount: formatMoney(
                  booking.priceSnapshot.totalAmount,
                  booking.priceSnapshot.currency,
                ),
              })
        }
        loading={pay.isPending}
        disabled={timeIsUp}
        onPress={() => pay.mutate()}
        fullWidth
        testID="pay-button"
      />
      <OPText variant="caption" tone="secondary">
        {t('payments.pay.secure')}
      </OPText>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    card: {
      gap: theme.spacing.sm,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      borderWidth: StyleSheet.hairlineWidth,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
  });
