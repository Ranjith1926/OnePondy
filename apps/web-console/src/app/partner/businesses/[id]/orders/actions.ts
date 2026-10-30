'use server';

import { redirect } from 'next/navigation';

import { partnerApi } from '@/lib/api';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { isOrderStep, stepNeedsReason } from '@/lib/foodOrders';

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
}

/**
 * One action on one order: accept, reject, start preparing, mark ready or cancel. The order's `version` comes from the inbox row
 * the restaurant is looking at, so if the order has changed since (the customer cancelled, the restaurant's other tablet accepted it)
 * the API refuses and the restaurant is told, instead of acting on an order they have not seen.
 */
export async function changeOrderStatus(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = text(form, 'businessId');
  const id = text(form, 'id');
  const step = form.get('step');
  if (!isOrderStep(step)) {
    return { status: 'error', message: 'Unknown action.' };
  }

  // An empty field must not become 0: Number('') is 0, which would pass for a version that was never sent.
  const rawVersion = text(form, 'version');
  const version = rawVersion === '' ? Number.NaN : Number(rawVersion);
  if (!Number.isInteger(version) || version < 0) {
    return {
      status: 'error',
      message: 'This order is out of date. Reload the page and try again.',
    };
  }

  const reason = text(form, 'reason');
  if (stepNeedsReason(step) && !reason) {
    return validationState({
      reason: [
        step === 'reject' ? 'Tell the customer why.' : 'Tell the customer why you are cancelling.',
      ],
    });
  }

  try {
    await partnerApi(`/api/v1/partner/businesses/${businessId}/orders/${id}/${step}`, {
      method: 'POST',
      // Only a rejection or a cancellation has a reason; the other steps send just the version.
      json: stepNeedsReason(step) ? { version, reason } : { version },
    });
  } catch (error) {
    return failureState(error);
  }

  const view = text(form, 'view');
  const query = new URLSearchParams({ done: step });
  if (view) {
    query.set('view', view);
  }
  redirect(`/partner/businesses/${businessId}/orders?${query.toString()}`);
}
