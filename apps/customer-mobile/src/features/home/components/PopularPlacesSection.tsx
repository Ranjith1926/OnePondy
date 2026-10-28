import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';

import { HorizontalCarousel, SectionWithHeader } from '../../../design-system';
import { PlaceSummaryCard } from '../../tourism/components/PlaceSummaryCard';
import { usePlaces } from '../../tourism/hooks/usePlaces';

const CARD_WIDTH = 232;

interface PopularPlacesSectionProps {
  onSeeAll: () => void;
}

export function PopularPlacesSection({ onSeeAll }: PopularPlacesSectionProps) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  // A verified place is "popular" here only in the sense of "shown first"; there is no visit or
  // booking data yet to rank by (spec §4: never invent signals the backend cannot back up).
  const { data } = usePlaces({ pageSize: 8 });
  const places = data?.items ?? [];
  if (places.length === 0) {
    return null;
  }

  return (
    <SectionWithHeader
      title={t('home.popular.title')}
      actionLabel={t('common.seeAll')}
      onAction={onSeeAll}
    >
      <HorizontalCarousel
        data={places}
        itemWidth={CARD_WIDTH}
        keyExtractor={(place) => place.id}
        renderItem={(place) => (
          <PlaceSummaryCard
            place={place}
            variant="carousel"
            onPress={() => navigation.navigate('PlaceDetail', { idOrSlug: place.slug })}
          />
        )}
        accessibilityLabel={t('home.popular.title')}
      />
    </SectionWithHeader>
  );
}
