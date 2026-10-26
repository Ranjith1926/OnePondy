import { useNavigation } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';

import {
  EmptyState,
  MainTabTemplate,
  OPChip,
  OPText,
  SearchBar,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { categoryIcons, placeCategoryIds, type PlaceCategoryId } from '../categories';
import { PlaceSummaryCard } from '../components/PlaceSummaryCard';
import { samplePlaces } from '../data/samplePlaces';

type CategoryFilter = PlaceCategoryId | 'all';

export function ExploreScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const [category, setCategory] = useState<CategoryFilter>('all');
  const places =
    category === 'all'
      ? samplePlaces
      : samplePlaces.filter((place) => place.categoryId === category);

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
            icon={categoryIcons[id]}
            selected={category === id}
            onPress={() => setCategory(id)}
          />
        ))}
      </ScrollView>
      {places.length > 0 ? (
        <View style={styles.list}>
          <OPText variant="caption" tone="secondary">
            {t('explore.sampleNotice')}
          </OPText>
          {places.map((place) => (
            <PlaceSummaryCard key={place.id} place={place} variant="list" />
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
