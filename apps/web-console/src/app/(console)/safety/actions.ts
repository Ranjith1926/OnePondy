'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { asGuid, asVersion } from '@/lib/ids';
import { isSafetyStep } from '@/lib/safety';
import { requireSupport } from '@/lib/session';

const MAX_NOTE_LENGTH = 1000;

/** Takes a report, or closes it with a note. The report's version is the one the person is looking at; if it moved on since, the API refuses. */
export async function changeSafetyReport(_previous: FormState, form: FormData): Promise<FormState> {
  await requireSupport();
  const id = asGuid(form.get('id'));
  const step = form.get('step');
  const version = asVersion(form.get('version'));
  if (!id || !isSafetyStep(step)) {
    return { status: 'error', message: 'Unknown action.' };
  }

  if (version === null) {
    return {
      status: 'error',
      message: 'This report is out of date. Reload the page and try again.',
    };
  }

  const raw = form.get('note');
  const note = typeof raw === 'string' ? raw.trim() : '';
  if (step === 'resolve') {
    if (!note) {
      return validationState({ note: ['Write what was done, for the record.'] });
    }
    if (note.length > MAX_NOTE_LENGTH) {
      return validationState({ note: [`Keep it to ${MAX_NOTE_LENGTH} characters or fewer.`] });
    }
  }

  try {
    await api(`/api/v1/admin/ride-safety/${id}/${step}`, {
      method: 'POST',
      json: step === 'resolve' ? { version, note } : { version },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/safety/${id}?done=${step}`);
}
