'use server';

import { redirect } from 'next/navigation';

import { partnerApi } from '@/lib/api';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { asGuid, asVersion } from '@/lib/ids';

const MAX_REPLY_LENGTH = 1000;

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
}

/**
 * The business answers a published review, once. The review's `version` comes from the row the owner is looking at, so if the author has
 * changed the review since (which clears it and hides it until it is checked again) the API refuses and the owner is told, instead of
 * their answer landing under words they have not read. An answer cannot be changed afterwards, so the form says so.
 */
export async function replyToReview(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = asGuid(form.get('businessId'));
  const id = asGuid(form.get('id'));
  if (!businessId || !id) {
    return { status: 'error', message: 'Unknown review.' };
  }

  const version = asVersion(form.get('version'));
  if (version === null) {
    return {
      status: 'error',
      message: 'This review is out of date. Reload the page and try again.',
    };
  }

  const reply = text(form, 'text');
  if (!reply) {
    return validationState({ text: ['Write your reply.'] });
  }

  if (reply.length > MAX_REPLY_LENGTH) {
    return validationState({ text: [`Keep it to ${MAX_REPLY_LENGTH} characters or fewer.`] });
  }

  try {
    await partnerApi(`/api/v1/partner/businesses/${businessId}/reviews/${id}/reply`, {
      method: 'POST',
      json: { version, text: reply },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/partner/businesses/${businessId}/reviews?done=reply`);
}
