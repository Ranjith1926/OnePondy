import { z } from 'zod';

// Messages are i18n keys; screens translate them when showing field errors.

/**
 * A 10-digit Indian mobile number (spec D1: SMS is India-only). Spaces and dashes are ignored, and
 * a pasted or autofilled +91, 91 or leading 0 is dropped.
 */
export const phoneSchema = z.object({
  phone: z
    .string()
    .transform((value) => value.replace(/[\s-]/g, '').replace(/^(?:\+?91|0)(?=\d{10}$)/, ''))
    .pipe(z.string().regex(/^[6-9]\d{9}$/, 'auth.validation.phone')),
});

export const codeSchema = z.object({
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, 'auth.validation.code'),
});

export type PhoneForm = z.input<typeof phoneSchema>;
export type CodeForm = z.input<typeof codeSchema>;

export type ValidationMessageKey = 'auth.validation.phone' | 'auth.validation.code';

export function isValidationMessageKey(value: unknown): value is ValidationMessageKey {
  return value === 'auth.validation.phone' || value === 'auth.validation.code';
}
