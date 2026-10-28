import { useTranslation } from 'react-i18next';

import { PlaceCard } from '../../../design-system';
import { resolveMediaUrl, type PlaceSummary } from '../api/placesApi';
import { categoryIcons, isKnownCategory } from '../categories';

interface PlaceSummaryCardProps {
  place: PlaceSummary;
  variant: 'carousel' | 'list';
  onPress?: () => void;
}

/** Shows distance when the list was fetched with a position, e.g. "Beach · 1.2 km away". */
function formatDistance(km: number): string {
  return km < 1 ? `${Math.round(km * 1000)} m` : `${km.toFixed(1)} km`;
}

export function PlaceSummaryCard({ place, variant, onPress }: PlaceSummaryCardProps) {
  const { t } = useTranslation();
  const categoryName = isKnownCategory(place.categoryCode)
    ? t(`categoryNames.${place.categoryCode}`)
    : place.categoryCode;
  const subtitle =
    place.distanceKm === null
      ? categoryName
      : t('common.placeSubtitleWithDistance', {
          category: categoryName,
          distance: formatDistance(place.distanceKm),
        });

  return (
    <PlaceCard
      variant={variant}
      title={place.name}
      subtitle={subtitle}
      fallbackIcon={
        isKnownCategory(place.categoryCode) ? categoryIcons[place.categoryCode] : 'map-pin'
      }
      imageSource={
        place.coverImage ? { uri: resolveMediaUrl(place.coverImage.urls.medium) } : undefined
      }
      onPress={onPress}
    />
  );
}
