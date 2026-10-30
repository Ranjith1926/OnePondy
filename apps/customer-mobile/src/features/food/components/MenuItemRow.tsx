import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPBadge, OPIconButton, OPText, useThemedStyles, type Theme } from '../../../design-system';
import { formatMoney } from '../../payments/formatMoney';
import type { MenuItem } from '../api/foodApi';
import { MAX_PER_DISH } from '../cart/cart';

interface Props {
  item: MenuItem;
  quantity: number;
  onAdd: () => void;
  onRemove: () => void;
}

/** One dish: its name, whether it is vegetarian, its price and a way to add or remove it. A sold-out dish cannot be added. */
export function MenuItemRow({ item, quantity, onAdd, onRemove }: Props) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);

  return (
    <View style={styles.row} testID={`dish-${item.id}`}>
      <View style={styles.text}>
        <View style={styles.titleLine}>
          <OPText variant="label" style={styles.name}>
            {item.name}
          </OPText>
          <OPBadge
            label={item.isVeg ? t('food.menu.veg') : t('food.menu.nonVeg')}
            tone={item.isVeg ? 'success' : 'error'}
          />
        </View>
        {item.description ? <OPText tone="secondary">{item.description}</OPText> : null}
        <OPText>{formatMoney(item.price, item.currency)}</OPText>
      </View>

      {item.isAvailable ? (
        <View style={styles.stepper}>
          {quantity > 0 ? (
            <>
              <OPIconButton
                icon="minus"
                variant="tonal"
                accessibilityLabel={t('food.menu.removeOne', { name: item.name })}
                onPress={onRemove}
              />
              <OPText
                variant="label"
                accessibilityLabel={t('food.menu.quantity', { count: quantity })}
              >
                {quantity}
              </OPText>
            </>
          ) : null}
          <OPIconButton
            icon="plus"
            variant="tonal"
            accessibilityLabel={t('food.menu.addOne', { name: item.name })}
            disabled={quantity >= MAX_PER_DISH}
            onPress={onAdd}
          />
        </View>
      ) : (
        <OPBadge label={t('food.menu.soldOut')} tone="neutral" />
      )}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
      paddingVertical: theme.spacing.xs,
    },
    text: {
      flex: 1,
      gap: theme.spacing.xxs,
    },
    titleLine: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xs,
      flexWrap: 'wrap',
    },
    name: {
      flexShrink: 1,
    },
    stepper: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xs,
    },
  });
