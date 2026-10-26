import { useTranslation } from 'react-i18next';

import { PlaceCard } from '../../../design-system';
import { categoryIcons } from '../categories';
import type { PlaceSummary } from '../data/samplePlaces';

interface PlaceSummaryCardProps {
  place: PlaceSummary;
  variant: 'carousel' | 'list';
}

export function PlaceSummaryCard({ place, variant }: PlaceSummaryCardProps) {
  const { t } = useTranslation();
  return (
    <PlaceCard
      variant={variant}
      title={place.name}
      subtitle={t('common.placeSubtitle', {
        category: t(`categoryNames.${place.categoryId}`),
        area: t(`areas.${place.areaId}`),
      })}
      fallbackIcon={categoryIcons[place.categoryId]}
      badgeLabel={place.isSample ? t('common.sample') : undefined}
    />
  );
}
