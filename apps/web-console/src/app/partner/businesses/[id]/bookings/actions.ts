'use server';

import { redirect } from 'next/navigation';

import { partnerApi } from '@/lib/api';
import type { BookingDetail } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';

const steps = ['accept', 'reject', 'cancel', 'complete'] as const;
type Step = (typeof steps)[number];

const isStep = (value: unknown): value is Step =>
  typeof value === 'string' && (steps as readonly string[]).includes(value);

function optional(form: FormData, name: string): string | null {
  const value = form.get(name);
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

export async function changeBookingStatus(
  _previous: FormState,
  form: FormData,
): Promise<FormState> {
  const businessId = String(form.get('businessId') ?? '');
  const id = String(form.get('id') ?? '');
  const step = form.get('step');
  if (!isStep(step)) {
    return { status: 'error', message: 'Unknown action.' };
  }

  const reason = optional(form, 'reason');
  if (step === 'reject' && !reason) {
    return validationState({ reason: ['Tell the customer why.'] });
  }

  try {
    await partnerApi<BookingDetail>(
      `/api/v1/partner/businesses/${businessId}/bookings/${id}/${step}`,
      {
        method: 'POST',
        json: { version: Number(form.get('version')), reason },
      },
    );
  } catch (error) {
    return failureState(error);
  }

  redirect(`/partner/businesses/${businessId}/bookings?done=${step}`);
}
