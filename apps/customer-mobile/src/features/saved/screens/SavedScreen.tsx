import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  EmptyState,
  ListItem,
  ListSection,
  MainTabTemplate,
  SectionWithHeader,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';
import { useItineraries } from '../../itineraries/hooks/useItineraries';
import { useSavedListings } from '../../listings/hooks/useSavedListings';
import { listingTypeIcons } from '../../listings/listingTypes';
import { PlaceSummaryCard } from '../../tourism/components/PlaceSummaryCard';
import { useSavedPlaces } from '../hooks/useSavedPlaces';

// Saved places and trip plans live together here (spec §10, Phase 4); saving and building a trip
// both need an account (spec §4 Guest access).
export function SavedScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const savedPlaces = useSavedPlaces();
  const itineraries = useItineraries(false);
  const savedListings = useSavedListings();

  if (auth.status !== 'authenticated') {
    return (
      <MainTabTemplate title={t('saved.title')}>
        <SignInPrompt
          icon="heart"
          title={t('saved.guest.title')}
          message={t('saved.guest.message')}
        />
      </MainTabTemplate>
    );
  }

  const places = savedPlaces.data ?? [];
  const trips = itineraries.data ?? [];
  const listings = savedListings.data ?? [];
  const stillLoading = savedPlaces.isPending || itineraries.isPending || savedListings.isPending;
  const nothingSaved =
    !stillLoading && places.length === 0 && trips.length === 0 && listings.length === 0;

  return (
    <MainTabTemplate title={t('saved.title')}>
      {nothingSaved ? (
        <EmptyState
          icon="heart"
          title={t('saved.empty.title')}
          message={t('saved.empty.message')}
          actionLabel={t('saved.empty.action')}
          onAction={() => navigation.navigate('Main', { screen: 'Explore' })}
        />
      ) : null}

      {trips.length > 0 ? (
        <SectionWithHeader title={t('saved.tripsTitle')}>
          <ListSection>
            {trips.map((trip) => (
              <ListItem
                key={trip.id}
                leadingIcon="map"
                title={trip.title}
                subtitle={t('itineraries.stopCount', { count: trip.stopCount })}
                trailing={{ kind: 'chevron' }}
                onPress={() => navigation.navigate('ItineraryDetail', { id: trip.id })}
              />
            ))}
          </ListSection>
        </SectionWithHeader>
      ) : null}

      {listings.length > 0 ? (
        <SectionWithHeader title={t('saved.listingsTitle')}>
          <ListSection>
            {listings.map((saved) => (
              <ListItem
                key={saved.listing.id}
                leadingIcon={listingTypeIcons[saved.listing.listingType]}
                title={saved.listing.title}
                subtitle={saved.listing.businessName}
                trailing={{ kind: 'chevron' }}
                onPress={() => navigation.navigate('ListingDetail', { id: saved.listing.id })}
              />
            ))}
          </ListSection>
        </SectionWithHeader>
      ) : null}

      {places.length > 0 ? (
        <SectionWithHeader title={t('saved.placesTitle')}>
          <View style={styles.places}>
            {places.map((saved) => (
              <PlaceSummaryCard
                key={saved.place.id}
                place={saved.place}
                variant="list"
                onPress={() => navigation.navigate('PlaceDetail', { idOrSlug: saved.place.slug })}
              />
            ))}
          </View>
        </SectionWithHeader>
      ) : null}
    </MainTabTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    places: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
    },
  });
