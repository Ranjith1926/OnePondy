import { useNavigation } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';

import {
  EmptyState,
  ErrorState,
  LoadingState,
  MainTabTemplate,
  OPChip,
  SearchBar,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { placeCategoryIds, type PlaceCategoryId } from '../categories';
import { PlaceSummaryCard } from '../components/PlaceSummaryCard';
import { usePlaces } from '../hooks/usePlaces';

type CategoryFilter = PlaceCategoryId | 'all';

export function ExploreScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const [category, setCategory] = useState<CategoryFilter>('all');
  const places = usePlaces({ category: category === 'all' ? undefined : category, pageSize: 50 });

  return (
    <MainTabTemplate title={t('explore.title')}>
      <View style={styles.inset}>
        <SearchBar
          placeholder={t('search.placeholder')}
          accessibilityHint={t('home.searchHint')}
          onPress={() => navigation.navigate('Search')}
        />
      </View>
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        accessibilityLabel={t('explore.categoriesLabel')}
        contentContainerStyle={styles.chips}
      >
        <OPChip
          label={t('categories.all')}
          selected={category === 'all'}
          onPress={() => setCategory('all')}
        />
        {placeCategoryIds.map((id) => (
          <OPChip
            key={id}
            label={t(`categories.${id}`)}
            selected={category === id}
            onPress={() => setCategory(id)}
          />
        ))}
      </ScrollView>
      {places.isPending ? (
        <View style={styles.list}>
          <LoadingState accessibilityLabel={t('explore.loading')} />
        </View>
      ) : places.isError ? (
        <ErrorState
          title={t('explore.error.title')}
          message={t('explore.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void places.refetch()}
          retrying={places.isFetching}
        />
      ) : places.data.items.length > 0 ? (
        <View style={styles.list}>
          {places.data.items.map((place) => (
            <PlaceSummaryCard
              key={place.id}
              place={place}
              variant="list"
              onPress={() => navigation.navigate('PlaceDetail', { idOrSlug: place.slug })}
            />
          ))}
        </View>
      ) : (
        <EmptyState
          icon="search-x"
          title={t('explore.empty.title')}
          message={t('explore.empty.message')}
          actionLabel={category === 'all' ? undefined : t('explore.empty.action')}
          onAction={category === 'all' ? undefined : () => setCategory('all')}
        />
      )}
    </MainTabTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    chips: {
      gap: theme.spacing.xs,
      paddingHorizontal: theme.layout.screenPadding,
      paddingVertical: theme.spacing.xxs,
    },
    list: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
    },
  });
