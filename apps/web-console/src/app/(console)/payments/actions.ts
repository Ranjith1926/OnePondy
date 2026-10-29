'use server';

import { revalidatePath } from 'next/cache';

import { api } from '@/lib/api';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { requireAdministrator } from '@/lib/session';

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Closes a reconciliation exception with a note of what was done about it. */
export async function resolveException(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  const id = form.get('id');
  const note = form.get('note');
  if (typeof id !== 'string' || !guidPattern.test(id)) {
    return { status: 'error', message: 'Unknown item.' };
  }
  if (typeof note !== 'string' || !note.trim()) {
    return validationState({ note: ['Say what you did about it.'] });
  }

  try {
    await api(`/api/v1/admin/payments/exceptions/${id}/resolve`, {
      method: 'POST',
      json: { note: note.trim() },
    });
  } catch (error) {
    return failureState(error);
  }

  revalidatePath('/payments/exceptions');
  return { status: 'success', message: 'Marked as resolved.' };
}
