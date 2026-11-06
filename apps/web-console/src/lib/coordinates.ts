/** Latitude and longitude as people type them into a form: plain decimal degrees, "11.9341" or "79,8306" are both accepted. */

export type CoordinateKind = 'latitude' | 'longitude';

const limits: Record<CoordinateKind, number> = { latitude: 90, longitude: 180 };

/** The number typed, or null when it is empty, not a number or outside the range for that kind of coordinate. */
export function parseCoordinate(text: string, kind: CoordinateKind): number | null {
  const cleaned = text.trim().replace(',', '.');
  if (cleaned === '' || !/^[-+]?\d+(\.\d+)?$/.test(cleaned)) {
    return null;
  }
  const value = Number(cleaned);
  return Math.abs(value) <= limits[kind] ? value : null;
}

/** Six decimal places is about a tenth of a metre, more than any address needs. */
export function formatCoordinate(value: number): string {
  return value.toFixed(6);
}

/** Where a map should start: on the typed position when both are valid, otherwise on the town. */
export function startingPoint(
  latitudeText: string,
  longitudeText: string,
  fallback: { latitude: number; longitude: number },
): { latitude: number; longitude: number; hasPin: boolean } {
  const latitude = parseCoordinate(latitudeText, 'latitude');
  const longitude = parseCoordinate(longitudeText, 'longitude');
  return latitude !== null && longitude !== null
    ? { latitude, longitude, hasPin: true }
    : { ...fallback, hasPin: false };
}
