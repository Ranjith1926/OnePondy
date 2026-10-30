import type { TFunction } from 'i18next';

import { isDriverValidationKey } from '../schemas/driverSchema';

/** Translates a schema validation message (stored as an i18n key) for display under a field. */
export function driverFieldError(t: TFunction, message: string | undefined): string | undefined {
  return isDriverValidationKey(message) ? t(message) : undefined;
}
