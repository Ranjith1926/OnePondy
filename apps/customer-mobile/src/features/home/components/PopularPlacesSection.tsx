import { useTranslation } from 'react-i18next';

import { HorizontalCarousel, SectionWithHeader } from '../../../design-system';
import { PlaceSummaryCard } from '../../tourism/components/PlaceSummaryCard';
import { samplePlaces } from '../../tourism/data/samplePlaces';

const CARD_WIDTH = 232;

interface PopularPlacesSectionProps {
  onSeeAll: () => void;
}

export function PopularPlacesSection({ onSeeAll }: PopularPlacesSectionProps) {
  const { t } = useTranslation();
  const places = samplePlaces.slice(0, 6);
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
        renderItem={(place) => <PlaceSummaryCard place={place} variant="carousel" />}
        accessibilityLabel={t('home.popular.title')}
      />
    </SectionWithHeader>
  );
}
