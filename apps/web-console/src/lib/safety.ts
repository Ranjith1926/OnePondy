/** The lists the staff queue can show. The API decides what each means. */
export type SafetyView = 'open' | 'resolved' | 'all';

export const safetyViews: readonly { key: SafetyView; label: string }[] = [
  { key: 'open', label: 'Waiting' },
  { key: 'resolved', label: 'Resolved' },
  { key: 'all', label: 'All' },
];

export const parseSafetyView = (value: string | undefined): SafetyView =>
  safetyViews.find((view) => view.key === value)?.key ?? 'open';

export type SafetyStep = 'acknowledge' | 'resolve';

export const isSafetyStep = (value: unknown): value is SafetyStep =>
  value === 'acknowledge' || value === 'resolve';

/** A map link for where the person was when they pressed the button. Coordinates only ever come from the API as numbers. */
export function mapLink(latitude: number | null, longitude: number | null): string | null {
  if (
    latitude === null ||
    longitude === null ||
    !Number.isFinite(latitude) ||
    !Number.isFinite(longitude)
  ) {
    return null;
  }

  return `https://www.openstreetmap.org/?mlat=${latitude}&mlon=${longitude}#map=17/${latitude}/${longitude}`;
}
