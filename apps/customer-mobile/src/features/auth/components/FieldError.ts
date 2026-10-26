import type { TFunction } from 'i18next';

import { isValidationMessageKey } from '../schemas/authSchemas';

/** Translates a schema validation message (stored as an i18n key) for display under a field. */
export function fieldError(t: TFunction, message: string | undefined): string | undefined {
  return isValidationMessageKey(message) ? t(message) : undefined;
}
