'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import type { DriverCashRow } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { requireAdministrator } from '@/lib/session';

/** Only ids shaped like the API's GUIDs are put into request paths. */
function guid(value: FormDataEntryValue | null): string | null {
  const text = typeof value === 'string' ? value : '';
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(text) ? text : null;
}

function optional(form: FormData, name: string): string | null {
  const value = form.get(name);
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

/**
 * Records cash a driver handed in. The API checks it against what the driver holds (it can never be more), so this only reads the form:
 * an amount that is a plain number with at most two decimals, and the optional receipt reference and note.
 */
export async function recordDeposit(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  const driverUserId = guid(form.get('driverUserId'));
  if (!driverUserId) {
    return { status: 'error', message: 'Unknown driver.' };
  }

  const raw = typeof form.get('amount') === 'string' ? (form.get('amount') as string).trim() : '';
  if (!/^\d{1,7}(\.\d{1,2})?$/.test(raw) || Number(raw) <= 0) {
    return validationState({
      amount: ['Enter the cash received, more than 0, with at most two decimal places.'],
    });
  }

  try {
    await api<DriverCashRow>(`/api/v1/admin/cash/drivers/${driverUserId}/deposits`, {
      method: 'POST',
      json: {
        amount: Number(raw),
        reference: optional(form, 'reference'),
        note: optional(form, 'note'),
      },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect('/cash?recorded=1');
}
