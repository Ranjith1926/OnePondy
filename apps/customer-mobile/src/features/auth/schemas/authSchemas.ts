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

export const emailSchema = z.object({
  email: z
    .string()
    .trim()
    .toLowerCase()
    .email('auth.validation.email')
    .max(254, 'auth.validation.email'),
});

export const codeSchema = z.object({
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, 'auth.validation.code'),
});

export const nameSchema = z.object({
  displayName: z.string().trim().min(1, 'auth.validation.name').max(120, 'auth.validation.name'),
});

export type PhoneForm = z.input<typeof phoneSchema>;
export type EmailForm = z.input<typeof emailSchema>;
export type CodeForm = z.input<typeof codeSchema>;
export type NameForm = z.input<typeof nameSchema>;

export type ValidationMessageKey =
  | 'auth.validation.phone'
  | 'auth.validation.email'
  | 'auth.validation.code'
  | 'auth.validation.name';

export function isValidationMessageKey(value: unknown): value is ValidationMessageKey {
  return (
    value === 'auth.validation.phone' ||
    value === 'auth.validation.email' ||
    value === 'auth.validation.code' ||
    value === 'auth.validation.name'
  );
}
