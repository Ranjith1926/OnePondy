import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPDivider, OPText, useThemedStyles, type Theme } from '../../../design-system';
import { formatMoney } from '../../payments/formatMoney';
import type { OrderLine, OrderPrice } from '../api/foodApi';

interface Props {
  lines: OrderLine[];
  price: OrderPrice;
}

function Row({
  label,
  amount,
  strong = false,
}: {
  label: string;
  amount: string;
  strong?: boolean;
}) {
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.row}>
      <OPText variant={strong ? 'label' : 'body'} style={styles.label}>
        {label}
      </OPText>
      <OPText variant={strong ? 'label' : 'body'}>{amount}</OPText>
    </View>
  );
}

/** Every dish, then GST, fees and the total, so the customer sees exactly what they pay before they commit (and again afterwards). */
export function PriceBreakdown({ lines, price }: Props) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const money = (amount: number) => formatMoney(amount, price.currency);

  return (
    <View style={styles.group} testID="price-breakdown">
      {lines.map((line) => (
        <Row
          key={line.menuItemId}
          label={`${line.quantity} × ${line.name}`}
          amount={money(line.subtotal)}
        />
      ))}
      <OPDivider />
      <Row label={t('food.checkout.gst')} amount={money(price.taxAmount)} />
      <Row label={t('food.checkout.deliveryFee')} amount={money(price.deliveryFee)} />
      <Row label={t('food.checkout.packagingFee')} amount={money(price.packagingFee)} />
      {price.discountAmount > 0 ? (
        <Row label={t('food.checkout.discount')} amount={`-${money(price.discountAmount)}`} />
      ) : null}
      <OPDivider />
      <Row label={t('food.checkout.total')} amount={money(price.totalAmount)} strong />
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    group: {
      gap: theme.spacing.xs,
    },
    row: {
      flexDirection: 'row',
      justifyContent: 'space-between',
      gap: theme.spacing.sm,
    },
    label: {
      flexShrink: 1,
    },
  });
