export type ModuleKey =
  'food' | 'rides' | 'stays' | 'activities' | 'tripPlanner' | 'notifications' | 'support';

export type ModuleStatus = 'available' | 'comingSoon' | 'hidden';

export const moduleKeys: readonly ModuleKey[] = [
  'food',
  'rides',
  'stays',
  'activities',
  'tripPlanner',
  'notifications',
  'support',
];

/**
 * Modules this app version can actually open. A module the server marks "available" but this
 * version cannot open stays "coming soon", so a flag can never promise more than the app delivers.
 */
export const implementedModules: ReadonlySet<ModuleKey> = new Set<ModuleKey>();

const isStatus = (value: unknown): value is ModuleStatus =>
  value === 'available' || value === 'comingSoon' || value === 'hidden';

/**
 * What each module shows, from the flags in /api/v1/config (spec §4, Phase 3). Without server
 * flags (first launch offline, or the API unreachable) everything is "coming soon": a module is
 * never shown as available before it is.
 */
export function effectiveModuleStatus(
  serverFlags: Readonly<Record<string, unknown>> | undefined,
): Record<ModuleKey, ModuleStatus> {
  const result = {} as Record<ModuleKey, ModuleStatus>;
  for (const key of moduleKeys) {
    const flag = serverFlags?.[key];
    const status: ModuleStatus = isStatus(flag) ? flag : 'comingSoon';
    result[key] = status === 'available' && !implementedModules.has(key) ? 'comingSoon' : status;
  }
  return result;
}
