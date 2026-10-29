import type { IconName } from '../../design-system';
import type { ListingType } from './api/listingsApi';

export const listingTypes: readonly ListingType[] = [
  'activity',
  'stay',
  'event',
  'service',
  'shop',
];

export const listingTypeIcons: Record<ListingType, IconName> = {
  activity: 'ticket',
  stay: 'bed-double',
  event: 'calendar-days',
  service: 'briefcase',
  shop: 'store',
};
