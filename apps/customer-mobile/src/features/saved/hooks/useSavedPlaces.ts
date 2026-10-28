import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';

import { useAppState } from '../../../state';
import { savedPlacesApi, type SavedPlace } from '../api/savedPlacesApi';

export const savedPlacesQueryKey = (lang: string) => ['saved-places', lang] as const;

/** Only queried while signed in: saving needs an account (spec §4 Guest access). */
export function useSavedPlaces() {
  const { i18n } = useTranslation();
  const { auth } = useAppState();
  return useQuery({
    queryKey: savedPlacesQueryKey(i18n.language),
    queryFn: () => savedPlacesApi.list(i18n.language),
    enabled: auth.status === 'authenticated',
  });
}

/** True while this place is saved, kept in sync with the saved-places list. */
export function useIsSaved(placeId: string | undefined): boolean {
  const { data } = useSavedPlaces();
  return placeId !== undefined && (data?.some((saved) => saved.place.id === placeId) ?? false);
}

export function useToggleSavedPlace() {
  const { i18n } = useTranslation();
  const queryClient = useQueryClient();
  const key = savedPlacesQueryKey(i18n.language);

  return useMutation({
    mutationFn: ({ placeId, save }: { placeId: string; save: boolean }) =>
      save ? savedPlacesApi.save(placeId) : savedPlacesApi.remove(placeId),
    // Toggling feels instant; a failure reverts it once the server call comes back.
    onMutate: async ({ placeId, save }) => {
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<SavedPlace[]>(key);
      if (save) {
        // The place card is enough for an optimistic entry; the real fetch refills the rest.
        queryClient.setQueryData<SavedPlace[]>(key, (current) => current ?? []);
      } else {
        queryClient.setQueryData<SavedPlace[]>(key, (current) =>
          current?.filter((saved) => saved.place.id !== placeId),
        );
      }
      return { previous };
    },
    onError: (_error, _variables, context) => {
      queryClient.setQueryData(key, context?.previous);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: key });
    },
  });
}
