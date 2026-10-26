export type ModuleKey =
  | 'food'
  | 'rides'
  | 'stays'
  | 'activities'
  | 'tripPlanner'
  | 'notifications'
  | 'account'
  | 'support';

export type ModuleStatus = 'available' | 'comingSoon' | 'hidden';

// Local defaults until module flags are served from /api/v1/config (spec §4, Phase 3).
// A module is never shown as available before its partners and operations are ready.
export const moduleStatus: Record<ModuleKey, ModuleStatus> = {
  food: 'comingSoon',
  rides: 'comingSoon',
  stays: 'comingSoon',
  activities: 'comingSoon',
  tripPlanner: 'comingSoon',
  notifications: 'comingSoon',
  account: 'comingSoon',
  support: 'comingSoon',
};
