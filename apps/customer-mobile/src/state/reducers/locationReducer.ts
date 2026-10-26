import { ActionTypes } from '../actionTypes';
import type { LocationState } from '../appState';
import type { AppAction } from '../rootActions';

export function locationReducer(state: LocationState, action: AppAction): LocationState {
  switch (action.type) {
    case ActionTypes.setSelectedLocation:
      // A manual choice replaces any previous coordinates; GPS history is never kept (spec §6).
      return {
        selectedAreaId: action.payload.areaId,
        latitude: null,
        longitude: null,
        source: 'manual',
      };
    default:
      return state;
  }
}
