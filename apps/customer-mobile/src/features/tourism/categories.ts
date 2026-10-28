import type { IconName } from '../../design-system';

export const placeCategoryIds = ['beach', 'heritage', 'spiritual', 'museum', 'nature'] as const;

export type PlaceCategoryId = (typeof placeCategoryIds)[number];

export const categoryIcons: Record<PlaceCategoryId, IconName> = {
  beach: 'waves',
  heritage: 'castle',
  spiritual: 'flower-2',
  museum: 'landmark',
  nature: 'trees',
};

/** The server may add categories the console supports before this app's next release. */
export function isKnownCategory(code: string): code is PlaceCategoryId {
  return (placeCategoryIds as readonly string[]).includes(code);
}
