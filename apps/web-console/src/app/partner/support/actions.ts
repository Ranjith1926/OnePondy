'use server';

import { redirect } from 'next/navigation';

import { partnerApi } from '@/lib/api';
import type { RequesterTicketDetail } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { asGuid } from '@/lib/ids';

const MAX_SUBJECT_LENGTH = 120;
const MAX_BODY_LENGTH = 2000;
const topics = ['booking', 'foodOrder', 'payment', 'account', 'business', 'other'] as const;

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
}

/**
 * A business owner asks support for help about one of their businesses. The API checks that the business is theirs and that the module
 * is open; this only reads the form: a business (a GUID, never anything that could change a request path), a topic from the known list,
 * a subject and a message within their limits.
 */
export async function createRequest(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = asGuid(form.get('businessId'));
  const topic = text(form, 'topic');
  const subject = text(form, 'subject');
  const message = text(form, 'message');

  const errors: Record<string, string[]> = {};
  if (!businessId) {
    errors.businessId = ['Choose the business this is about.'];
  }

  if (!(topics as readonly string[]).includes(topic)) {
    errors.topic = ['Choose what this is about.'];
  }

  if (!subject) {
    errors.subject = ['Write a few words about it.'];
  } else if (subject.length > MAX_SUBJECT_LENGTH) {
    errors.subject = [`Keep it to ${MAX_SUBJECT_LENGTH} characters or fewer.`];
  }

  if (!message) {
    errors.message = ['Tell us what happened.'];
  } else if (message.length > MAX_BODY_LENGTH) {
    errors.message = [`Keep it to ${MAX_BODY_LENGTH} characters or fewer.`];
  }

  if (Object.keys(errors).length > 0) {
    return validationState(errors);
  }

  let created: RequesterTicketDetail;
  try {
    created = await partnerApi<RequesterTicketDetail>('/api/v1/support/tickets', {
      method: 'POST',
      json: { topic, subject, message, businessId },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/partner/support/${created.id}?done=created`);
}

/** The owner adds to a request. It goes back in support's queue; one resolved long ago cannot be reopened and the API says so. */
export async function replyToRequest(_previous: FormState, form: FormData): Promise<FormState> {
  const id = asGuid(form.get('id'));
  if (!id) {
    return { status: 'error', message: 'Unknown request.' };
  }

  const body = text(form, 'body');
  if (!body) {
    return validationState({ body: ['Write your reply.'] });
  }

  if (body.length > MAX_BODY_LENGTH) {
    return validationState({ body: [`Keep it to ${MAX_BODY_LENGTH} characters or fewer.`] });
  }

  try {
    await partnerApi(`/api/v1/support/tickets/${id}/messages`, { method: 'POST', json: { body } });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/partner/support/${id}?done=reply`);
}
