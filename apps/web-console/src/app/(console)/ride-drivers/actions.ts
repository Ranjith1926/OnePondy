'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import type { RideDriverDetail } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { asGuid, asVersion } from '@/lib/ids';
import { requireAdministrator } from '@/lib/session';

const steps = ['approve', 'reject', 'suspend', 'reinstate'] as const;
type Step = (typeof steps)[number];

const isStep = (value: unknown): value is Step =>
  typeof value === 'string' && (steps as readonly string[]).includes(value);

export async function changeRideDriverStatus(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  await requireAdministrator();
  const id = asGuid(form.get('id'));
  const step = form.get('step');
  const version = asVersion(form.get('version'));
  if (!id || !isStep(step) || version === null) {
    return { status: 'error', message: 'Unknown action.' };
  }

  const raw = form.get('reason');
  const reason = typeof raw === 'string' && raw.trim() ? raw.trim() : null;
  if ((step === 'reject' || step === 'suspend') && !reason) {
    return validationState({ reason: ['Tell the driver why.'] });
  }

  try {
    await api<RideDriverDetail>(`/api/v1/admin/ride-drivers/${id}/${step}`, {
      method: 'POST',
      json: step === 'reject' || step === 'suspend' ? { version, reason } : { version },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/ride-drivers/${id}?done=${step}`);
}
