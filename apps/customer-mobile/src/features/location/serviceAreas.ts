// Provisional manual area list. Service areas will come from the API once content
// management exists (spec Phase 3); GPS-based location arrives with Phase 4.
export const serviceAreaIds = [
  'puducherry',
  'whiteTown',
  'tamilQuarter',
  'auroville',
  'lawspet',
  'muthialpet',
  'reddiarpalayam',
  'ariyankuppam',
  'villianur',
] as const;

export type AreaId = (typeof serviceAreaIds)[number];

/** Used until the user picks an area: the whole city. */
export const defaultAreaId: AreaId = 'puducherry';

export function isAreaId(value: string | null): value is AreaId {
  return value !== null && (serviceAreaIds as readonly string[]).includes(value);
}
