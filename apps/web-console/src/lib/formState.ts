import 'server-only';

import { unstable_rethrow } from 'next/navigation';

import { ApiError } from './api';

/** What a form's server function returns to `useActionState`. */
export interface FormState {
  status: 'idle' | 'success' | 'error';
  message?: string;
  fieldErrors?: Record<string, string[]>;
}

export const idleState: FormState = { status: 'idle' };

/**
 * Turns an API problem into form feedback. Next.js redirects and not-found signals are rethrown,
 * and unexpected errors are logged and reported without internal details.
 */
export function failureState(error: unknown): FormState {
  unstable_rethrow(error);
  if (error instanceof ApiError) {
    return { status: 'error', message: error.message, fieldErrors: error.fieldErrors };
  }
  console.error(error);
  return {
    status: 'error',
    message: 'Something went wrong. Check that the API is running and try again.',
  };
}

export function validationState(fieldErrors: Record<string, string[]>): FormState {
  return { status: 'error', message: 'Check the highlighted fields.', fieldErrors };
}
