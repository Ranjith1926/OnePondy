import type { AreaId } from '../../location/serviceAreas';
import type { PlaceCategoryId } from '../categories';

export interface PlaceSummary {
  id: string;
  name: string;
  categoryId: PlaceCategoryId;
  areaId: AreaId;
  /** Sample content is always labelled in the UI (spec §4). */
  isSample: boolean;
}

// Real place names used as clearly labelled sample content. No hours, prices, ratings or
// availability are shown for them. Verified places replace this list in Phase 4.
const SAMPLE_PLACES: readonly PlaceSummary[] = [
  {
    id: 'sample-promenade-beach',
    name: 'Promenade Beach',
    categoryId: 'beach',
    areaId: 'whiteTown',
    isSample: true,
  },
  {
    id: 'sample-sri-aurobindo-ashram',
    name: 'Sri Aurobindo Ashram',
    categoryId: 'spiritual',
    areaId: 'whiteTown',
    isSample: true,
  },
  {
    id: 'sample-matrimandir',
    name: 'Matrimandir',
    categoryId: 'spiritual',
    areaId: 'auroville',
    isSample: true,
  },
  {
    id: 'sample-french-war-memorial',
    name: 'French War Memorial',
    categoryId: 'heritage',
    areaId: 'whiteTown',
    isSample: true,
  },
  {
    id: 'sample-pondicherry-museum',
    name: 'Pondicherry Museum',
    categoryId: 'museum',
    areaId: 'whiteTown',
    isSample: true,
  },
  {
    id: 'sample-manakula-vinayagar-temple',
    name: 'Manakula Vinayagar Temple',
    categoryId: 'spiritual',
    areaId: 'whiteTown',
    isSample: true,
  },
  {
    id: 'sample-sacred-heart-basilica',
    name: 'Basilica of the Sacred Heart of Jesus',
    categoryId: 'heritage',
    areaId: 'tamilQuarter',
    isSample: true,
  },
  {
    id: 'sample-paradise-beach',
    name: 'Paradise Beach',
    categoryId: 'beach',
    areaId: 'puducherry',
    isSample: true,
  },
  {
    id: 'sample-botanical-garden',
    name: 'Botanical Garden',
    categoryId: 'nature',
    areaId: 'puducherry',
    isSample: true,
  },
  {
    id: 'sample-ousteri-lake',
    name: 'Ousteri Lake',
    categoryId: 'nature',
    areaId: 'villianur',
    isSample: true,
  },
];

/** Development builds only: production builds show empty states instead of sample data. */
export const samplePlaces: readonly PlaceSummary[] = __DEV__ ? SAMPLE_PLACES : [];

/**
 * Case-insensitive match on the place name and any extra labels the caller provides,
 * such as translated area and category names.
 */
export function filterPlaces(
  places: readonly PlaceSummary[],
  query: string,
  labelsFor: (place: PlaceSummary) => string[],
): PlaceSummary[] {
  const needle = query.trim().toLocaleLowerCase();
  if (!needle) {
    return [];
  }
  return places.filter((place) =>
    [place.name, ...labelsFor(place)].some((text) => text.toLocaleLowerCase().includes(needle)),
  );
}
