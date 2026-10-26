/** Parses "major.minor.patch"; anything else is null. */
export function parseVersion(value: string | undefined | null): [number, number, number] | null {
  const match = /^(\d{1,5})\.(\d{1,5})\.(\d{1,5})$/.exec(value?.trim() ?? '');
  return match ? [Number(match[1]), Number(match[2]), Number(match[3])] : null;
}

/**
 * True when this app's version is below the server's minimum. Unreadable versions never block:
 * a malformed setting must not lock everyone out of the app.
 */
export function isBelowMinimum(
  appVersion: string | undefined,
  minimum: string | undefined,
): boolean {
  const app = parseVersion(appVersion);
  const min = parseVersion(minimum);
  if (!app || !min) {
    return false;
  }
  for (let index = 0; index < 3; index++) {
    if (app[index] !== min[index]) {
      return app[index] < min[index];
    }
  }
  return false;
}
