import { useNavigation } from '@react-navigation/native';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FlatList, StyleSheet, View } from 'react-native';

import {
  EmptyState,
  OPChip,
  OPIconButton,
  OPSearchField,
  OPText,
  SearchResultsTemplate,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { PlaceSummaryCard } from '../components/PlaceSummaryCard';
import { filterPlaces, samplePlaces } from '../data/samplePlaces';

const suggestionKeys = ['beach', 'temple', 'auroville'] as const;

export function SearchScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const [query, setQuery] = useState('');
  const trimmed = query.trim();

  const results = useMemo(
    () =>
      filterPlaces(samplePlaces, trimmed, (place) => [
        t(`areas.${place.areaId}`),
        t(`categoryNames.${place.categoryId}`),
      ]),
    [trimmed, t],
  );

  return (
    <SearchResultsTemplate
      header={
        <>
          <OPIconButton
            icon="arrow-left"
            accessibilityLabel={t('common.back')}
            onPress={() => navigation.goBack()}
          />
          <View style={styles.field}>
            <OPSearchField
              value={query}
              onChangeText={setQuery}
              placeholder={t('search.placeholder')}
              clearLabel={t('common.clearSearch')}
              autoFocus
            />
          </View>
        </>
      }
    >
      {!trimmed ? (
        <View style={styles.suggestions}>
          <OPText variant="label" tone="secondary" accessibilityRole="header">
            {t('search.suggestionsTitle')}
          </OPText>
          <View style={styles.chips}>
            {suggestionKeys.map((key) => (
              <OPChip
                key={key}
                label={t(`search.suggestions.${key}`)}
                onPress={() => setQuery(t(`search.suggestions.${key}`))}
              />
            ))}
          </View>
        </View>
      ) : results.length > 0 ? (
        <FlatList
          data={results}
          keyExtractor={(place) => place.id}
          renderItem={({ item }) => <PlaceSummaryCard place={item} variant="list" />}
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={styles.results}
          ListHeaderComponent={
            <OPText variant="caption" tone="secondary" accessibilityLiveRegion="polite">
              {t('search.resultsCount', { count: results.length })}
            </OPText>
          }
        />
      ) : (
        <EmptyState
          icon="search-x"
          title={t('search.noResults.title', { query: trimmed })}
          message={t('search.noResults.message')}
        />
      )}
    </SearchResultsTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    field: {
      flex: 1,
    },
    suggestions: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
      paddingTop: theme.spacing.md,
    },
    chips: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.xs,
    },
    results: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
      paddingTop: theme.spacing.xs,
      paddingBottom: theme.spacing.xxl,
    },
  });
