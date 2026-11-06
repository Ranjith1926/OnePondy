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
  OPText,
  SearchBar,
  useThemedStyles,
  type MapMarker,
  type Theme,
} from '../../../design-system';
import { AppMap } from '../../maps/components/AppMap';
import type { PlaceSummary } from '../api/placesApi';
import { placeCategoryIds, type PlaceCategoryId } from '../categories';
import { PlaceSummaryCard } from '../components/PlaceSummaryCard';
import { usePlaces } from '../hooks/usePlaces';

type CategoryFilter = PlaceCategoryId | 'all';
type ViewMode = 'list' | 'map';

/** A place can be pinned only once it has a position. */
type MappablePlace = PlaceSummary & { latitude: number; longitude: number };
const hasPosition = (place: PlaceSummary): place is MappablePlace =>
  place.latitude !== null && place.longitude !== null;

export function ExploreScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const [category, setCategory] = useState<CategoryFilter>('all');
  const [mode, setMode] = useState<ViewMode>('list');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const places = usePlaces({ category: category === 'all' ? undefined : category, pageSize: 50 });
  const openPlace = (place: PlaceSummary) =>
    navigation.navigate('PlaceDetail', { idOrSlug: place.slug });

  return (
    <MainTabTemplate title={t('explore.title')}>
      <View style={styles.inset}>
        <SearchBar
          placeholder={t('search.placeholder')}
          accessibilityHint={t('home.searchHint')}
          onPress={() => navigation.navigate('Search')}
        />
      </View>
      <View style={styles.modes}>
        <OPChip
          label={t('explore.viewList')}
          selected={mode === 'list'}
          onPress={() => setMode('list')}
        />
        <OPChip
          label={t('explore.viewMap')}
          selected={mode === 'map'}
          onPress={() => setMode('map')}
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
      ) : places.data.items.length > 0 && mode === 'map' ? (
        <MapOfPlaces
          places={places.data.items.filter(hasPosition)}
          category={category}
          selectedId={selectedId}
          onSelect={setSelectedId}
          onOpen={openPlace}
        />
      ) : places.data.items.length > 0 ? (
        <View style={styles.list}>
          {places.data.items.map((place) => (
            <PlaceSummaryCard
              key={place.id}
              place={place}
              variant="list"
              onPress={() => openPlace(place)}
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

interface MapOfPlacesProps {
  places: MappablePlace[];
  category: CategoryFilter;
  selectedId: string | null;
  onSelect: (id: string) => void;
  onOpen: (place: PlaceSummary) => void;
}

/** The places that have a position, as pins; pressing a pin shows that place's card under the map. */
function MapOfPlaces({ places, category, selectedId, onSelect, onOpen }: MapOfPlacesProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  // A pin picked under another filter is no longer on the map, so nothing is selected.
  const selected = places.find((place) => place.id === selectedId);

  if (places.length === 0) {
    return (
      <View style={styles.list}>
        <OPText tone="secondary">{t('explore.mapEmpty')}</OPText>
      </View>
    );
  }

  const markers: MapMarker[] = places.map((place) => ({
    id: place.id,
    latitude: place.latitude,
    longitude: place.longitude,
    label: place.name,
    selected: place.id === selected?.id,
  }));
  return (
    <View style={styles.list}>
      <AppMap
        markers={markers}
        accessibilityLabel={t('explore.mapLabel')}
        height={360}
        // Picking a pin must not move the view; a different set of pins starts from a fresh fit.
        viewKey={`${category}:${places.map((place) => place.id).join(',')}`}
        onMarkerPress={onSelect}
      />
      {selected ? (
        <PlaceSummaryCard place={selected} variant="list" onPress={() => onOpen(selected)} />
      ) : null}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    modes: {
      flexDirection: 'row',
      gap: theme.spacing.xs,
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
