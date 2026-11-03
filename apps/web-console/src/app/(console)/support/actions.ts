'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import type { SupportContact } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { asGuid, asVersion } from '@/lib/ids';
import { requireSupport } from '@/lib/session';
import { isTicketStep, ticketStepNeedsReason, type ContactState } from '@/lib/support';

const MAX_BODY_LENGTH = 2000;
const MAX_REASON_LENGTH = 200;

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
}

const OUT_OF_DATE = 'This request is out of date. Reload the page and try again.';

/**
 * Support answers a request. The request's `version` comes from the page the agent is looking at, so if the person wrote again, or another
 * agent answered, since, the API refuses and the agent is told, instead of answering words they have not read.
 */
export async function replyToTicket(_previous: FormState, form: FormData): Promise<FormState> {
  await requireSupport();
  const id = asGuid(form.get('id'));
  if (!id) {
    return { status: 'error', message: 'Unknown request.' };
  }

  const version = asVersion(form.get('version'));
  if (version === null) {
    return { status: 'error', message: OUT_OF_DATE };
  }

  const body = text(form, 'body');
  if (!body) {
    return validationState({ body: ['Write your reply.'] });
  }

  if (body.length > MAX_BODY_LENGTH) {
    return validationState({ body: [`Keep it to ${MAX_BODY_LENGTH} characters or fewer.`] });
  }

  try {
    await api(`/api/v1/admin/support/tickets/${id}/messages`, {
      method: 'POST',
      json: { version, body },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/support/${id}?done=reply`);
}

/** One action on one request: take it, release it, resolve it, or escalate it (which needs a reason). */
export async function changeTicket(_previous: FormState, form: FormData): Promise<FormState> {
  await requireSupport();
  const id = asGuid(form.get('id'));
  const step = form.get('step');
  if (!id || !isTicketStep(step)) {
    return { status: 'error', message: 'Unknown action.' };
  }

  const version = asVersion(form.get('version'));
  if (version === null) {
    return { status: 'error', message: OUT_OF_DATE };
  }

  const reason = text(form, 'reason');
  if (ticketStepNeedsReason(step) && !reason) {
    return validationState({ reason: ['Say why, for the administrator who reads it.'] });
  }

  if (reason.length > MAX_REASON_LENGTH) {
    return validationState({ reason: [`Keep it to ${MAX_REASON_LENGTH} characters or fewer.`] });
  }

  try {
    await api(`/api/v1/admin/support/tickets/${id}/${step}`, {
      method: 'POST',
      json: ticketStepNeedsReason(step) ? { version, reason } : { version },
    });
  } catch (error) {
    return failureState(error);
  }

  // A resolved request has nothing left to do on its page, so the agent goes back to the queue; the rest stay on the request.
  redirect(step === 'resolve' ? '/support?done=resolve' : `/support/${id}?done=${step}`);
}

/**
 * Shows how to reach the person. Opening a request does not do this, because the conversation is where support talks to them; asking is a
 * separate step, and the API records each time (spec Phase 10: support sees only what a request needs, and every access is audited).
 */
export async function revealContact(
  _previous: ContactState,
  form: FormData,
): Promise<ContactState> {
  await requireSupport();
  const id = asGuid(form.get('id'));
  if (!id) {
    return { status: 'error', message: 'Unknown request.' };
  }

  try {
    const contact = await api<SupportContact>(`/api/v1/admin/support/tickets/${id}/contact`);
    return { status: 'shown', phoneNumber: contact.phoneNumber, email: contact.email };
  } catch (error) {
    const failure = failureState(error);
    return { status: 'error', message: failure.message };
  }
}
