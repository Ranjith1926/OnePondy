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
import { useSecondsLeft } from '../hooks/useCountdown';
import { formatCountdown, formatMoney } from '../formatMoney';
import type { PayOutcome, Payable } from '../paymentFlow';

/** The part of a payment mutation the panel needs, so it works for a booking and for a food order. */
export interface PayState<T extends Payable> {
  mutate: () => void;
  isPending: boolean;
  isError: boolean;
  data: PayOutcome<T> | undefined;
}

interface Props<T extends Payable> {
  amount: number;
  currency: string;
  /** When the time to pay runs out. */
  expiresAt: string | null;
  pay: PayState<T>;
  /** What to tell the customer, since it is about a booking or an order. */
  messages: { accepted: string; timeUp: string; lapsed: string };
  /** Turns a failure to even start paying into words. */
  errorMessage: (error: unknown) => string;
}

const outcomeBanners: Record<Exclude<PayOutcome['kind'], 'error'>, BannerKind> = {
  confirmed: 'success',
  processing: 'info',
  cancelled: 'warning',
  failed: 'error',
  lapsed: 'warning',
};

/**
 * Shown while the partner has accepted and the customer still has to pay. The button opens the gateway's checkout; whatever it
 * reports, the booking or order is then re-read from the server, which alone decides if it is paid.
 */
export function PaymentPanel<T extends Payable>({
  amount,
  currency,
  expiresAt,
  pay,
  messages,
  errorMessage,
}: Props<T>) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const secondsLeft = useSecondsLeft(expiresAt);
  const timeIsUp = secondsLeft !== null && secondsLeft <= 0;
  const outcome = pay.data;
  const money = formatMoney(amount, currency);

  return (
    <View style={styles.card}>
      <OPText variant="title" accessibilityRole="header">
        {t('payments.pay.title')}
      </OPText>
      <OPText tone="secondary">{messages.accepted}</OPText>
      <OPText variant="label">{money}</OPText>

      {secondsLeft !== null ? (
        <OPText tone={timeIsUp ? 'error' : 'secondary'} accessibilityLiveRegion="polite">
          {timeIsUp
            ? messages.timeUp
            : t('payments.pay.timeLeft', { time: formatCountdown(secondsLeft) })}
        </OPText>
      ) : null}

      {pay.isError ? <InlineBanner kind="error" message={t('payments.outcome.error')} /> : null}
      {outcome?.kind === 'error' ? (
        <InlineBanner kind="error" message={errorMessage(outcome.error)} />
      ) : null}
      {outcome && outcome.kind !== 'error' && outcome.kind !== 'confirmed' ? (
        <InlineBanner
          kind={outcomeBanners[outcome.kind]}
          message={
            outcome.kind === 'lapsed' ? messages.lapsed : t(`payments.outcome.${outcome.kind}`)
          }
        />
      ) : null}

      <OPButton
        label={
          pay.isPending ? t('payments.pay.opening') : t('payments.pay.button', { amount: money })
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
