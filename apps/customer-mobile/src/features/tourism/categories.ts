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
