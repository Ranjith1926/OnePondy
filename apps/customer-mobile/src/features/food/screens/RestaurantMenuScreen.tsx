import { useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  ErrorState,
  LoadingState,
  ModalSheet,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { formatMoney } from '../../payments/formatMoney';
import type { MenuItem, PublicRestaurant } from '../api/foodApi';
import { quantityOf, wouldReplaceCart, type CartRestaurant } from '../cart/cart';
import { useCart } from '../cart/CartContext';
import { CartBar } from '../components/CartBar';
import { MenuItemRow } from '../components/MenuItemRow';
import { useRestaurantMenu } from '../hooks/useFood';

type Props = StaticScreenProps<{ id: string }>;

function toCartRestaurant(restaurant: PublicRestaurant): CartRestaurant {
  return {
    businessId: restaurant.businessId,
    name: restaurant.name,
    minOrderAmount: restaurant.minOrderAmount,
    prepTimeMinutes: restaurant.prepTimeMinutes,
    deliveryAreas: restaurant.deliveryAreas,
  };
}

export function RestaurantMenuScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const menu = useRestaurantMenu(route.params.id);
  const { cart, dispatch } = useCart();
  // A dish from a different restaurant than the cart's waits here until the customer says whether to start a new cart.
  const [pending, setPending] = useState<MenuItem | null>(null);

  // Each time the menu is read, dishes that have gone or sold out leave the cart and prices follow the menu.
  useEffect(() => {
    if (!menu.data) {
      return;
    }

    dispatch({
      type: 'sync',
      businessId: menu.data.restaurant.businessId,
      available: menu.data.categories
        .flatMap((category) => category.items)
        .filter((item) => item.isAvailable && !item.isArchived)
        .map((item) => ({ id: item.id, name: item.name, isVeg: item.isVeg, price: item.price })),
    });
  }, [menu.data, dispatch]);

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
    </View>
  );

  if (menu.isPending) {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <View style={styles.centered}>
          <LoadingState accessibilityLabel={t('food.menu.loading')} />
        </View>
      </SafeAreaView>
    );
  }

  if (menu.isError) {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <ErrorState
          title={t('food.menu.error.title')}
          message={t('food.menu.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void menu.refetch()}
          retrying={menu.isFetching}
        />
      </SafeAreaView>
    );
  }

  const { restaurant, categories } = menu.data;

  const add = (item: MenuItem) => {
    if (wouldReplaceCart(cart, restaurant.businessId)) {
      setPending(item);
      return;
    }

    dispatch({
      type: 'add',
      restaurant: toCartRestaurant(restaurant),
      dish: { id: item.id, name: item.name, isVeg: item.isVeg, price: item.price },
    });
  };

  return (
    <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
      {header}
      <ScrollView contentContainerStyle={[styles.inset, styles.content]}>
        <OPText variant="title" accessibilityRole="header">
          {restaurant.name}
        </OPText>
        {restaurant.description ? <OPText tone="secondary">{restaurant.description}</OPText> : null}
        <OPText tone="secondary">
          {t('food.menu.about', {
            minutes: restaurant.prepTimeMinutes,
            amount: formatMoney(restaurant.minOrderAmount, 'INR'),
          })}
        </OPText>
        {restaurant.fssaiLicenseNumber ? (
          <OPText variant="caption" tone="secondary">
            {t('food.menu.fssai', { number: restaurant.fssaiLicenseNumber })}
          </OPText>
        ) : null}

        {categories.length === 0 ? (
          <OPText tone="secondary">{t('food.menu.empty')}</OPText>
        ) : (
          categories.map((category) => (
            <View key={category.id} style={styles.section}>
              <OPText variant="label" accessibilityRole="header">
                {category.name}
              </OPText>
              {category.items.map((item) => (
                <MenuItemRow
                  key={item.id}
                  item={item}
                  quantity={quantityOf(cart, item.id)}
                  onAdd={() => add(item)}
                  onRemove={() => dispatch({ type: 'remove', menuItemId: item.id })}
                />
              ))}
            </View>
          ))
        )}

        <OPText variant="caption" tone="secondary">
          {t('food.menu.estimateNote')}
        </OPText>
      </ScrollView>

      <CartBar />

      <ModalSheet
        visible={pending !== null}
        onClose={() => setPending(null)}
        title={t('food.menu.newCart.title')}
        closeLabel={t('common.close')}
      >
        <View style={styles.sheet}>
          <OPText tone="secondary">
            {t('food.menu.newCart.message', { name: cart.restaurant?.name ?? '' })}
          </OPText>
          <OPButton
            label={t('food.menu.newCart.confirm')}
            onPress={() => {
              if (pending) {
                dispatch({
                  type: 'add',
                  restaurant: toCartRestaurant(restaurant),
                  dish: {
                    id: pending.id,
                    name: pending.name,
                    isVeg: pending.isVeg,
                    price: pending.price,
                  },
                });
              }
              setPending(null);
            }}
            fullWidth
          />
          <OPButton
            label={t('food.menu.newCart.keep')}
            variant="tertiary"
            onPress={() => setPending(null)}
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
      gap: theme.spacing.xs,
      paddingBottom: theme.spacing.xl,
    },
    section: {
      marginTop: theme.spacing.md,
      gap: theme.spacing.xxs,
    },
    sheet: {
      gap: theme.spacing.sm,
    },
  });
