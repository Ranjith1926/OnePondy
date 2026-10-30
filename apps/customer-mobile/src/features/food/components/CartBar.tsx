import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPButton, useThemedStyles, type Theme } from '../../../design-system';
import { useAppState } from '../../../state';
import { formatMoney } from '../../payments/formatMoney';
import { cartEstimate, cartItemCount } from '../cart/cart';
import { useCart } from '../cart/CartContext';

/**
 * A bar along the bottom of the food screens while the cart has dishes. Checking out needs an account, so a guest is taken to sign in
 * first; the cart is kept, so they find it again afterwards.
 */
export function CartBar() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const { cart } = useCart();
  const count = cartItemCount(cart);
  if (count === 0) {
    return null;
  }

  return (
    <View style={styles.bar}>
      <OPButton
        label={t('food.cart.viewCart', { count, amount: formatMoney(cartEstimate(cart), 'INR') })}
        onPress={() => navigation.navigate(auth.status === 'authenticated' ? 'Checkout' : 'SignIn')}
        fullWidth
        testID="cart-bar"
      />
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    bar: {
      paddingHorizontal: theme.layout.screenPadding,
      paddingVertical: theme.spacing.sm,
      borderTopWidth: StyleSheet.hairlineWidth,
      borderTopColor: theme.colors.border,
      backgroundColor: theme.colors.background,
    },
  });
