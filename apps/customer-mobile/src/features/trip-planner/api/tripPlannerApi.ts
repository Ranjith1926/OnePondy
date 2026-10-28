import { apiRequest } from '../../../api/apiClient';
import type { ItineraryDetail } from '../../itineraries/api/itinerariesApi';

export type TripPace = 'relaxed' | 'balanced' | 'packed';
export type TripBudgetBand = 'low' | 'medium' | 'high';

export interface TripPlanInput {
  startDate: string;
  endDate: string;
  interests: string[];
  pace: TripPace;
  budgetBand: TripBudgetBand;
  mobilityNeeds?: string;
  lat?: number;
  lng?: number;
}

export const tripPlannerApi = {
  /** Returns the new AI-assisted draft itinerary (spec §7); the client treats it like any other itinerary from there. */
  plan: (input: TripPlanInput, lang: string) =>
    apiRequest<ItineraryDetail>(`/api/v1/trip-planner/plan?lang=${encodeURIComponent(lang)}`, {
      method: 'POST',
      body: input,
      auth: true,
    }),
};
