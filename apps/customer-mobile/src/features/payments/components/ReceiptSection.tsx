import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPText, useThemedStyles, type Theme } from '../../../design-system';
import type { BookingDetail, RefundInfo } from '../../bookings/api/bookingsApi';
import { formatDateTime } from '../../bookings/formatDateTime';
import { formatMoney } from '../formatMoney';

interface Props {
  booking: Pick<BookingDetail, 'payment' | 'refunds'>;
}

function refundKey(refund: RefundInfo): 'processed' | 'inProgress' {
  // A failed refund is being sorted out by the team, so to the customer it is still on its way.
  return refund.status === 'processed' ? 'processed' : 'inProgress';
}

/** What was paid and what has come back, so the customer never has to wonder (spec Phase 7: receipts, refund status). */
export function ReceiptSection({ booking }: Props) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const { payment, refunds } = booking;

  if (!payment && refunds.length === 0) {
    return null;
  }

  return (
    <View style={styles.section}>
      {payment ? (
        <View style={styles.group} testID="receipt">
          <OPText variant="label">{t('payments.receipt.title')}</OPText>
          <OPText>
            {t('payments.receipt.paid', {
              amount: formatMoney(payment.amount, payment.currency),
              date: formatDateTime(payment.paidAt),
            })}
          </OPText>
          <OPText tone="secondary">
            {t('payments.receipt.reference', { reference: payment.reference })}
          </OPText>
        </View>
      ) : null}

      {refunds.map((refund, index) => (
        <View key={`${refund.requestedAt}-${index}`} style={styles.group} testID="refund">
          <OPText variant="label">{t('payments.refund.title')}</OPText>
          <OPText>
            {t(`payments.refund.${refundKey(refund)}`, {
              amount: formatMoney(refund.amount, payment?.currency ?? 'INR'),
              date: refund.processedAt ? formatDateTime(refund.processedAt) : '',
            })}
          </OPText>
          <OPText tone="secondary">
            {t(
              refund.status === 'processed'
                ? 'payments.refund.bankDelay'
                : 'payments.refund.willBeNotified',
            )}
          </OPText>
        </View>
      ))}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    section: {
      marginTop: theme.spacing.md,
      gap: theme.spacing.md,
    },
    group: {
      gap: theme.spacing.xxs,
    },
  });
