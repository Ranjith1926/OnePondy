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
import { formatDateTime } from '../../bookings/formatDateTime';
import { useRefetchOnFocus } from '../../bookings/hooks/useRefetchOnFocus';
import { isAreaId } from '../../location/serviceAreas';
import { PaymentPanel } from '../../payments/components/PaymentPanel';
import { ReceiptSection } from '../../payments/components/ReceiptSection';
import { usePayOrder } from '../../payments/hooks/usePayOrder';
import type { OrderDetail } from '../api/foodApi';
import { PriceBreakdown } from '../components/PriceBreakdown';
import { foodErrorMessageKey } from '../foodErrors';
import { useCancelOrder, useOrder } from '../hooks/useFood';

type Props = StaticScreenProps<{ id: string }>;

function AwaitingPayment({ order }: { order: OrderDetail }) {
  const { t } = useTranslation();
  const pay = usePayOrder(order.id, order.restaurantName);

  return (
    <PaymentPanel
      amount={order.price.totalAmount}
      currency={order.price.currency}
      expiresAt={order.expiresAt}
      pay={pay}
      messages={{
        accepted: t('food.pay.accepted'),
        timeUp: t('food.pay.timeUp'),
        lapsed: t('food.pay.lapsed'),
      }}
      errorMessage={(error) => t(foodErrorMessageKey(error))}
    />
  );
}

export function OrderDetailScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const order = useOrder(route.params.id);
  useRefetchOnFocus(order.refetch);
  const cancelOrder = useCancelOrder(route.params.id);
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

  if (order.isPending) {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <View style={styles.centered}>
          <LoadingState accessibilityLabel={t('food.order.loading')} />
        </View>
      </SafeAreaView>
    );
  }

  if (order.isError) {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <ErrorState
          title={t('food.order.error.title')}
          message={t('food.order.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void order.refetch()}
          retrying={order.isFetching}
        />
      </SafeAreaView>
    );
  }

  const data = order.data;

  return (
    <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
      {header}
      <ScrollView contentContainerStyle={[styles.inset, styles.content]}>
        <OPText variant="title" accessibilityRole="header">
          {data.restaurantName}
        </OPText>
        <OPText variant="label" accessibilityLiveRegion="polite">
          {t(`food.order.status.${data.status}`)}
        </OPText>
        <OPText tone="secondary">
          {t(
            // Never tell a customer they paid when nothing was paid (online payment can be off).
            data.status === 'confirmed' && data.payment
              ? 'food.order.hint.confirmedPaid'
              : `food.order.hint.${data.status}`,
          )}
        </OPText>
        {data.estimatedReadyAt &&
        (data.status === 'preparing' || data.status === 'readyForPickup') ? (
          <OPText>
            {t('food.order.readyBy', { time: formatDateTime(data.estimatedReadyAt) })}
          </OPText>
        ) : null}
        {data.closedReason ? (
          <OPText tone="secondary">{t('food.order.reason', { reason: data.closedReason })}</OPText>
        ) : null}

        {data.status === 'awaitingPayment' ? (
          <View style={styles.section}>
            <AwaitingPayment order={data} />
          </View>
        ) : null}

        <ReceiptSection booking={data} />

        <View style={styles.section}>
          <OPText variant="label">{t('food.order.items')}</OPText>
          <PriceBreakdown lines={data.lines} price={data.price} />
        </View>

        <View style={styles.section}>
          <OPText variant="label">{t('food.order.deliveringTo')}</OPText>
          <OPText>{data.deliveryAddress}</OPText>
          <OPText tone="secondary">
            {isAreaId(data.deliveryArea) ? t(`areas.${data.deliveryArea}`) : data.deliveryArea}
          </OPText>
          {data.deliveryNote ? <OPText tone="secondary">{data.deliveryNote}</OPText> : null}
        </View>

        {data.fssaiLicenseNumber ? (
          <OPText variant="caption" tone="secondary">
            {t('food.order.fssai', { number: data.fssaiLicenseNumber })}
          </OPText>
        ) : null}

        <View style={styles.section}>
          <OPText variant="label">{t('food.order.timeline')}</OPText>
          {data.history.map((entry, index) => (
            <View key={`${entry.status}-${entry.at}-${index}`} style={styles.timelineRow}>
              <OPText>{t(`food.order.status.${entry.status}`)}</OPText>
              <OPText tone="secondary">{formatDateTime(entry.at)}</OPText>
            </View>
          ))}
        </View>
      </ScrollView>

      {data.canCancel ? (
        <View style={[styles.inset, styles.footer]}>
          <OPButton
            label={t('food.order.cancel')}
            variant="secondary"
            onPress={() => setConfirming(true)}
            fullWidth
          />
        </View>
      ) : null}

      <ModalSheet
        visible={confirming}
        onClose={() => setConfirming(false)}
        title={t('food.order.cancelConfirm.title')}
        closeLabel={t('common.close')}
      >
        <View style={styles.confirmBody}>
          {cancelOrder.error ? (
            <InlineBanner kind="error" message={t(foodErrorMessageKey(cancelOrder.error))} />
          ) : null}
          <OPText tone="secondary">
            {data.payment
              ? t('food.order.cancelConfirm.paidMessage')
              : t('food.order.cancelConfirm.message')}
          </OPText>
          <OPButton
            label={
              cancelOrder.isPending
                ? t('food.order.cancelling')
                : t('food.order.cancelConfirm.confirm')
            }
            variant="danger"
            loading={cancelOrder.isPending}
            onPress={() =>
              cancelOrder.mutate(
                { version: data.version },
                { onSuccess: () => setConfirming(false) },
              )
            }
            fullWidth
          />
          <OPButton
            label={t('food.order.cancelConfirm.keep')}
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
