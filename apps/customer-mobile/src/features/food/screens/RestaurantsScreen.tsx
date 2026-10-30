import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  EmptyState,
  ErrorState,
  ListItem,
  LoadingState,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useSelectedArea } from '../../location/hooks/useSelectedArea';
import { formatMoney } from '../../payments/formatMoney';
import { CartBar } from '../components/CartBar';
import { useRestaurants } from '../hooks/useFood';

// Reached from the Food shortcut on Home, so it is a pushed screen with its own back button (spec §10, Phase 8).
export function RestaurantsScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const area = useSelectedArea();
  const restaurants = useRestaurants(area.areaId);

  return (
    <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
      <View style={styles.header}>
        <OPIconButton
          icon="arrow-left"
          accessibilityLabel={t('common.back')}
          onPress={() => navigation.goBack()}
        />
        <OPText variant="title" accessibilityRole="header">
          {t('food.restaurants.title')}
        </OPText>
      </View>

      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <View style={[styles.inset, styles.areaRow]}>
          <OPText tone="secondary" style={styles.areaText}>
            {t('food.restaurants.deliveringTo', { area: area.name })}
          </OPText>
          <OPButton
            label={t('food.restaurants.changeArea')}
            variant="tertiary"
            size="sm"
            onPress={() => navigation.navigate('LocationSelector')}
          />
        </View>

        {restaurants.isPending ? (
          <View style={styles.inset}>
            <LoadingState accessibilityLabel={t('food.restaurants.loading')} />
          </View>
        ) : restaurants.isError ? (
          <ErrorState
            title={t('food.restaurants.error.title')}
            message={t('food.restaurants.error.message')}
            retryLabel={t('common.retry')}
            onRetry={() => void restaurants.refetch()}
            retrying={restaurants.isFetching}
          />
        ) : restaurants.data.length > 0 ? (
          <View style={[styles.inset, styles.list]}>
            {restaurants.data.map((restaurant) => (
              <ListItem
                key={restaurant.businessId}
                leadingIcon="utensils"
                title={restaurant.name}
                subtitle={t('food.restaurants.details', {
                  minutes: restaurant.prepTimeMinutes,
                  amount: formatMoney(restaurant.minOrderAmount, 'INR'),
                })}
                trailing={{ kind: 'chevron' }}
                onPress={() => navigation.navigate('RestaurantMenu', { id: restaurant.businessId })}
              />
            ))}
          </View>
        ) : (
          <EmptyState
            icon="utensils"
            title={t('food.restaurants.empty.title')}
            message={t('food.restaurants.empty.message')}
            actionLabel={t('food.restaurants.changeArea')}
            onAction={() => navigation.navigate('LocationSelector')}
          />
        )}
      </ScrollView>

      <CartBar />
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
    content: {
      gap: theme.spacing.md,
      paddingBottom: theme.spacing.xl,
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    areaRow: {
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'space-between',
    },
    areaText: {
      flexShrink: 1,
    },
    list: {
      gap: theme.spacing.sm,
    },
  });
