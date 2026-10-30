import { StackActions, useNavigation } from '@react-navigation/native';
import { useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { isApiError } from '../../../api/apiErrors';
import {
  EmptyState,
  FormField,
  InlineBanner,
  LoadingState,
  OPButton,
  OPChip,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';
import { newIdempotencyKey } from '../../bookings/idempotencyKey';
import { formatMoney } from '../../payments/formatMoney';
import { cartLineInputs } from '../cart/cart';
import { useCart } from '../cart/CartContext';
import { PriceBreakdown } from '../components/PriceBreakdown';
import { servedAreas, startingArea } from '../deliveryAreas';
import { fieldError, foodErrorMessageKey } from '../foodErrors';
import { usePlaceOrder, useQuote } from '../hooks/useFood';

const MAX_ADDRESS = 300;
const MAX_NOTE = 200;

/**
 * Checks the cart out: the server prices it (the app never sends a price), the customer gives a delivery address and phone, and the
 * order is placed with the total they were shown. They are charged only after the restaurant accepts.
 */
export function CheckoutScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth, location } = useAppState();
  const { cart, dispatch } = useCart();
  const restaurant = cart.restaurant;

  const served = useMemo(
    () => (restaurant ? servedAreas(restaurant.deliveryAreas) : []),
    [restaurant],
  );
  const [chosenArea, setChosenArea] = useState<string | null>(null);
  const area =
    chosenArea !== null && (served as string[]).includes(chosenArea)
      ? chosenArea
      : startingArea(location.selectedAreaId, served);
  const [address, setAddress] = useState('');
  const [note, setNote] = useState('');
  const [phone, setPhone] = useState('');

  const lines = cartLineInputs(cart);
  const quote = useQuote(restaurant?.businessId, area, lines);
  const place = usePlaceOrder();

  // One key per attempt at the same cart and area, so a retry after a dropped connection can never make a second order. A changed
  // cart is a new attempt.
  const attempt = useRef<{ signature: string; key: string } | null>(null);

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
      <OPText variant="title" accessibilityRole="header">
        {t('food.checkout.title')}
      </OPText>
    </View>
  );

  if (auth.status !== 'authenticated') {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <SignInPrompt
          icon="utensils"
          title={t('food.checkout.guest.title')}
          message={t('food.checkout.guest.message')}
        />
      </SafeAreaView>
    );
  }

  if (!restaurant || cart.lines.length === 0) {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <EmptyState
          icon="utensils"
          title={t('food.checkout.empty.title')}
          message={t('food.checkout.empty.message')}
          actionLabel={t('food.checkout.empty.action')}
          onAction={() => navigation.navigate('Restaurants')}
        />
      </SafeAreaView>
    );
  }

  const quoted = quote.data;
  const belowMinimum = quoted !== undefined && quoted.price.subtotal < quoted.minOrderAmount;
  const ready =
    quoted !== undefined &&
    !belowMinimum &&
    area !== null &&
    address.trim().length > 0 &&
    phone.trim().length > 0;

  const submit = () => {
    if (!quoted || area === null) {
      return;
    }

    const signature = JSON.stringify([restaurant.businessId, area, lines]);
    if (attempt.current?.signature !== signature) {
      attempt.current = { signature, key: newIdempotencyKey() };
    }

    place.mutate(
      {
        idempotencyKey: attempt.current.key,
        businessId: restaurant.businessId,
        area,
        address: address.trim(),
        note: note.trim() === '' ? undefined : note.trim(),
        contactPhone: phone.trim(),
        lines,
        expectedTotal: quoted.price.totalAmount,
      },
      {
        onSuccess: (order) => {
          dispatch({ type: 'clear' });
          // Replace checkout, so back from the order goes to where the customer came from, not to an empty cart.
          navigation.dispatch(StackActions.replace('OrderDetail', { id: order.id }));
        },
        onError: (error) => {
          if (isApiError(error) && error.code === 'order_price_changed') {
            void quote.refetch();
          }
        },
      },
    );
  };

  const placeError = place.error;
  const priceChanged = isApiError(placeError) && placeError.code === 'order_price_changed';

  return (
    <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
      {header}
      <ScrollView
        contentContainerStyle={[styles.inset, styles.content]}
        keyboardShouldPersistTaps="handled"
      >
        <OPText variant="label">{restaurant.name}</OPText>

        <View style={styles.section}>
          <OPText variant="label">{t('food.checkout.deliverTo')}</OPText>
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.chips}
          >
            {served.map((id) => (
              <OPChip
                key={id}
                label={t(`areas.${id}`)}
                selected={id === area}
                onPress={() => setChosenArea(id)}
              />
            ))}
          </ScrollView>
        </View>

        <FormField
          label={t('food.checkout.address')}
          hint={t('food.checkout.addressHint')}
          error={fieldError(placeError, 'address')}
          value={address}
          onChangeText={setAddress}
          multiline
          maxLength={MAX_ADDRESS}
          autoComplete="street-address"
        />
        <FormField
          label={t('food.checkout.phone')}
          hint={t('food.checkout.phoneHint')}
          error={fieldError(placeError, 'contactPhone')}
          value={phone}
          onChangeText={setPhone}
          keyboardType="phone-pad"
          autoComplete="tel"
          textContentType="telephoneNumber"
        />
        <FormField
          label={t('food.checkout.note')}
          value={note}
          onChangeText={setNote}
          maxLength={MAX_NOTE}
        />

        <View style={styles.section}>
          <OPText variant="label" accessibilityRole="header">
            {t('food.checkout.pricing')}
          </OPText>
          {quote.isPending && quote.fetchStatus !== 'idle' ? (
            <LoadingState accessibilityLabel={t('food.checkout.pricingLoading')} />
          ) : quote.isError ? (
            <>
              <InlineBanner kind="error" message={t(foodErrorMessageKey(quote.error))} />
              <OPButton
                label={t('common.retry')}
                variant="secondary"
                onPress={() => void quote.refetch()}
                loading={quote.isFetching}
                fullWidth
              />
              <OPButton
                label={t('food.checkout.keepShopping')}
                variant="tertiary"
                onPress={() => navigation.goBack()}
                fullWidth
              />
            </>
          ) : quoted ? (
            <PriceBreakdown lines={quoted.lines} price={quoted.price} />
          ) : null}
        </View>

        {belowMinimum && quoted ? (
          <InlineBanner
            kind="warning"
            message={t('food.checkout.belowMinimum', {
              name: restaurant.name,
              minimum: formatMoney(quoted.minOrderAmount, quoted.price.currency),
              more: formatMoney(
                quoted.minOrderAmount - quoted.price.subtotal,
                quoted.price.currency,
              ),
            })}
          />
        ) : null}

        {place.isError ? (
          <InlineBanner
            kind="error"
            message={
              priceChanged && quoted
                ? t('food.checkout.priceChanged', {
                    amount: formatMoney(quoted.price.totalAmount, quoted.price.currency),
                  })
                : t(foodErrorMessageKey(placeError))
            }
          />
        ) : null}

        <OPText variant="caption" tone="secondary">
          {t('food.checkout.payLater', { name: restaurant.name })}
        </OPText>
      </ScrollView>

      <View style={[styles.inset, styles.footer]}>
        <OPButton
          label={place.isPending ? t('food.checkout.placing') : t('food.checkout.place')}
          loading={place.isPending}
          disabled={!ready || place.isPending}
          onPress={submit}
          fullWidth
          testID="place-order"
        />
      </View>
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
      gap: theme.spacing.xxs,
      paddingHorizontal: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    content: {
      gap: theme.spacing.md,
      paddingBottom: theme.spacing.xl,
    },
    section: {
      gap: theme.spacing.xs,
    },
    chips: {
      gap: theme.spacing.xs,
      paddingVertical: theme.spacing.xxs,
    },
    footer: {
      paddingVertical: theme.spacing.sm,
      borderTopWidth: StyleSheet.hairlineWidth,
      borderTopColor: theme.colors.border,
    },
  });
