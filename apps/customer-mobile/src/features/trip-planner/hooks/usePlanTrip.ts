import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';

import { tripPlannerApi, type TripPlanInput } from '../api/tripPlannerApi';

export function usePlanTrip() {
  const { i18n } = useTranslation();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: TripPlanInput) => tripPlannerApi.plan(input, i18n.language),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['itineraries'] }),
  });
}
