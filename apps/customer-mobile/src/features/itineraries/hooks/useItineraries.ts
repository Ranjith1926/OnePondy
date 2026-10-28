import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';

import {
  itinerariesApi,
  type AddStopInput,
  type ItineraryInput,
  type StopPosition,
} from '../api/itinerariesApi';

export const itinerariesQueryKey = (includeArchived: boolean) =>
  ['itineraries', includeArchived] as const;
export const itineraryQueryKey = (id: string, lang: string) => ['itinerary', id, lang] as const;

export function useItineraries(includeArchived = false) {
  return useQuery({
    queryKey: itinerariesQueryKey(includeArchived),
    queryFn: () => itinerariesApi.list(includeArchived),
  });
}

export function useItinerary(id: string | undefined) {
  const { i18n } = useTranslation();
  return useQuery({
    queryKey: itineraryQueryKey(id ?? '', i18n.language),
    queryFn: () => itinerariesApi.get(id!, i18n.language),
    enabled: id !== undefined,
  });
}

/** Invalidates both the list (stop counts, titles) and this one detail view after a change. */
function useInvalidateItinerary(id: string) {
  const { i18n } = useTranslation();
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['itineraries'] });
    void queryClient.invalidateQueries({ queryKey: itineraryQueryKey(id, i18n.language) });
  };
}

export function useCreateItinerary() {
  const { i18n } = useTranslation();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ItineraryInput) => itinerariesApi.create(input, i18n.language),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['itineraries'] }),
  });
}

export function useUpdateItinerary(id: string) {
  const { i18n } = useTranslation();
  const invalidate = useInvalidateItinerary(id);
  return useMutation({
    mutationFn: (input: ItineraryInput) => itinerariesApi.update(id, input, i18n.language),
    onSuccess: invalidate,
  });
}

export function useDeleteItinerary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => itinerariesApi.remove(id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['itineraries'] }),
  });
}

/** Moves an AI-assisted draft to saved (spec §7 step 5); a no-op server-side once already saved. */
export function useConfirmItinerary(id: string) {
  const { i18n } = useTranslation();
  const invalidate = useInvalidateItinerary(id);
  return useMutation({
    mutationFn: () => itinerariesApi.confirm(id, i18n.language),
    onSuccess: invalidate,
  });
}

export function useSetItineraryArchived(id: string) {
  const { i18n } = useTranslation();
  const invalidate = useInvalidateItinerary(id);
  return useMutation({
    mutationFn: (archived: boolean) =>
      archived
        ? itinerariesApi.archive(id, i18n.language)
        : itinerariesApi.unarchive(id, i18n.language),
    onSuccess: invalidate,
  });
}

export function useAddStop(itineraryId: string) {
  const { i18n } = useTranslation();
  const invalidate = useInvalidateItinerary(itineraryId);
  return useMutation({
    mutationFn: (input: AddStopInput) => itinerariesApi.addStop(itineraryId, input, i18n.language),
    onSuccess: invalidate,
  });
}

export function useRemoveStop(itineraryId: string) {
  const { i18n } = useTranslation();
  const invalidate = useInvalidateItinerary(itineraryId);
  return useMutation({
    mutationFn: (stopId: string) => itinerariesApi.removeStop(itineraryId, stopId, i18n.language),
    onSuccess: invalidate,
  });
}

/** Sends every stop's new day and order in one call, so a reorder never partially applies. */
export function useReorderStops(itineraryId: string) {
  const { i18n } = useTranslation();
  const invalidate = useInvalidateItinerary(itineraryId);
  return useMutation({
    mutationFn: (positions: StopPosition[]) =>
      itinerariesApi.reorderStops(itineraryId, positions, i18n.language),
    onSuccess: invalidate,
  });
}

export function useSharing(itineraryId: string) {
  const invalidate = useInvalidateItinerary(itineraryId);
  const enable = useMutation({
    mutationFn: () => itinerariesApi.enableSharing(itineraryId),
    onSuccess: invalidate,
  });
  const disable = useMutation({
    mutationFn: () => itinerariesApi.disableSharing(itineraryId),
    onSuccess: invalidate,
  });
  return { enable, disable };
}
