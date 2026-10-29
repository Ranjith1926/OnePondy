import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useAppState } from '../../../state';
import { savedListingsApi, type SavedListing } from '../api/savedListingsApi';

export const savedListingsQueryKey = ['saved-listings'] as const;

/** Only queried while signed in: saving needs an account (spec §4 Guest access). */
export function useSavedListings() {
  const { auth } = useAppState();
  return useQuery({
    queryKey: savedListingsQueryKey,
    queryFn: () => savedListingsApi.list(),
    enabled: auth.status === 'authenticated',
  });
}

/** True while this listing is saved, kept in sync with the saved-listings list. */
export function useIsListingSaved(listingId: string | undefined): boolean {
  const { data } = useSavedListings();
  return (
    listingId !== undefined && (data?.some((saved) => saved.listing.id === listingId) ?? false)
  );
}

export function useToggleSavedListing() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ listingId, save }: { listingId: string; save: boolean }) =>
      save ? savedListingsApi.save(listingId) : savedListingsApi.remove(listingId),
    // Removing feels instant; a failure puts it back once the server call comes back. Saving waits
    // for the refetch, since the list needs the whole listing, not only its id.
    onMutate: async ({ listingId, save }) => {
      await queryClient.cancelQueries({ queryKey: savedListingsQueryKey });
      const previous = queryClient.getQueryData<SavedListing[]>(savedListingsQueryKey);
      if (!save) {
        queryClient.setQueryData<SavedListing[]>(savedListingsQueryKey, (current) =>
          current?.filter((saved) => saved.listing.id !== listingId),
        );
      }
      return { previous };
    },
    onError: (_error, _variables, context) => {
      queryClient.setQueryData(savedListingsQueryKey, context?.previous);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: savedListingsQueryKey });
    },
  });
}
