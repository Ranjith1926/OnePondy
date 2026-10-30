'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import type { DriverDetail } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { requireAdministrator } from '@/lib/session';

const steps = ['approve', 'reject', 'suspend', 'reinstate'] as const;
type Step = (typeof steps)[number];

const isStep = (value: unknown): value is Step =>
  typeof value === 'string' && (steps as readonly string[]).includes(value);

/** Only ids shaped like the API's GUIDs are put into request paths. */
function guid(value: FormDataEntryValue | null): string | null {
  const text = typeof value === 'string' ? value : '';
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(text) ? text : null;
}

function optional(form: FormData, name: string): string | null {
  const value = form.get(name);
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

export async function changeDriverStatus(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  const id = guid(form.get('id'));
  const step = form.get('step');
  const version = Number(form.get('version'));
  if (!id || !isStep(step) || !Number.isInteger(version) || version <= 0) {
    return { status: 'error', message: 'Unknown action.' };
  }

  const reason = optional(form, 'reason');
  if ((step === 'reject' || step === 'suspend') && !reason) {
    return validationState({ reason: ['Tell the driver why.'] });
  }

  try {
    await api<DriverDetail>(`/api/v1/admin/drivers/${id}/${step}`, {
      method: 'POST',
      json: step === 'reject' || step === 'suspend' ? { version, reason } : { version },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/drivers/${id}?done=${step}`);
}
