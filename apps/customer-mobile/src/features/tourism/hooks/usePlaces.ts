import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';

import { placesApi, type PlacesQuery } from '../api/placesApi';

export const placesQueryKey = (query: PlacesQuery) => ['places', query] as const;

/** List of published, verified places (spec §10, Phase 4): the real content behind Explore and search. */
export function usePlaces(query: Omit<PlacesQuery, 'lang'> = {}) {
  const { i18n } = useTranslation();
  return useQuery({
    queryKey: placesQueryKey({ ...query, lang: i18n.language }),
    queryFn: () => placesApi.list({ ...query, lang: i18n.language }),
  });
}

export function usePlace(idOrSlug: string | undefined) {
  const { i18n } = useTranslation();
  return useQuery({
    queryKey: ['place', idOrSlug, i18n.language] as const,
    queryFn: () => placesApi.get(idOrSlug!, i18n.language),
    enabled: idOrSlug !== undefined,
  });
}
