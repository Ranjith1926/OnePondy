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
    case ActionTypes.setGpsLocation:
      return {
        ...state,
        latitude: action.payload.latitude,
        longitude: action.payload.longitude,
        source: 'gps',
      };
    case ActionTypes.clearGpsLocation:
      return state.source === 'gps'
        ? { ...state, latitude: null, longitude: null, source: null }
        : state;
    default:
      return state;
  }
}
