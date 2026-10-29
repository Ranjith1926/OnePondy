'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import type { SettlementDetail } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { requireAdministrator } from '@/lib/session';

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const datePattern = /^\d{4}-\d{2}-\d{2}$/;

/** Only ids shaped like the API's GUIDs are put into request paths. */
function guid(value: FormDataEntryValue | null): string | null {
  return typeof value === 'string' && guidPattern.test(value) ? value : null;
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
}

/** Builds a draft statement for one business and period from the ledger. */
export async function draftSettlement(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  const businessId = guid(form.get('businessId'));
  const periodStart = text(form, 'periodStart');
  const periodEnd = text(form, 'periodEnd');

  const errors: Record<string, string[]> = {};
  if (!businessId) {
    errors.businessId = ['Choose a business.'];
  }
  if (!datePattern.test(periodStart)) {
    errors.periodStart = ['Choose the first day.'];
  }
  if (!datePattern.test(periodEnd)) {
    errors.periodEnd = ['Choose the last day.'];
  }
  if (Object.keys(errors).length > 0) {
    return validationState(errors);
  }

  let created: SettlementDetail;
  try {
    created = await api<SettlementDetail>('/api/v1/admin/settlements', {
      method: 'POST',
      json: { businessId, periodStart, periodEnd },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/settlements/${created.settlement.id}?done=draft`);
}

const steps = ['approve', 'paid', 'discard'] as const;
type Step = (typeof steps)[number];

const isStep = (value: unknown): value is Step =>
  typeof value === 'string' && (steps as readonly string[]).includes(value);

/** Takes a statement through approve, mark paid (with the bank reference) or discard (drafts only). */
export async function changeSettlement(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  const id = guid(form.get('id'));
  const step = form.get('step');
  const version = Number(form.get('version'));
  if (!id || !isStep(step) || !Number.isInteger(version) || version < 0) {
    return { status: 'error', message: 'Unknown action.' };
  }

  const payoutReference = text(form, 'payoutReference');
  if (step === 'paid' && !payoutReference) {
    return validationState({ payoutReference: ['Enter the bank reference for the transfer.'] });
  }

  try {
    await api(`/api/v1/admin/settlements/${id}/${step}`, {
      method: 'POST',
      json: step === 'paid' ? { version, payoutReference } : { version },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(step === 'discard' ? '/settlements?done=discard' : `/settlements/${id}?done=${step}`);
}
