import type { IconName } from '../../../design-system';

export interface CollectionSummary {
  id: string;
  title: string;
  description: string;
  icon: IconName;
  isSample: boolean;
}

// Themed ideas shown as labelled sample content; they are not bookable products.
const SAMPLE_COLLECTIONS: readonly CollectionSummary[] = [
  {
    id: 'sample-heritage-walk',
    title: 'Heritage walk in White Town',
    description: 'Colonial streets, bright facades and quiet cafés around the French Quarter.',
    icon: 'footprints',
    isSample: true,
  },
  {
    id: 'sample-sunrise',
    title: 'Sunrise by the sea',
    description: 'Early mornings on the Promenade and the beaches nearby.',
    icon: 'sunrise',
    isSample: true,
  },
  {
    id: 'sample-auroville',
    title: 'Auroville and around',
    description: 'The Matrimandir, green forest paths and the villages around Auroville.',
    icon: 'sprout',
    isSample: true,
  },
  {
    id: 'sample-cafe-trail',
    title: 'Café and bakery trail',
    description: 'French-style bakeries and local cafés worth a slow afternoon.',
    icon: 'coffee',
    isSample: true,
  },
];

/** Development builds only (spec §4). */
export const sampleCollections: readonly CollectionSummary[] = __DEV__ ? SAMPLE_COLLECTIONS : [];
