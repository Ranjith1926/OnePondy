'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { asGuid, asVersion } from '@/lib/ids';
import { isReviewStep, parseReviewStatus, reviewStepNeedsReason } from '@/lib/reviews';
import { requireEditor } from '@/lib/session';

const MAX_REASON_LENGTH = 500;

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
}

/**
 * One decision on one review: publish it, reject it, or take a published one down. The review's `version` comes from the row the
 * moderator is looking at, so if it has changed since (another moderator decided, the author edited it) the API refuses and the
 * moderator is told, instead of deciding on words they have not read. Rejecting and taking down need a reason the author is shown.
 */
export async function moderateReview(_previous: FormState, form: FormData): Promise<FormState> {
  await requireEditor();
  const id = asGuid(form.get('id'));
  const step = form.get('step');
  if (!id || !isReviewStep(step)) {
    return { status: 'error', message: 'Unknown action.' };
  }

  const version = asVersion(form.get('version'));
  if (version === null) {
    return {
      status: 'error',
      message: 'This review is out of date. Reload the page and try again.',
    };
  }

  const reason = text(form, 'reason');
  if (reviewStepNeedsReason(step) && !reason) {
    return validationState({
      reason: [
        step === 'reject'
          ? 'Tell the author why it is not being published.'
          : 'Tell the author why it is being taken down.',
      ],
    });
  }

  if (reason.length > MAX_REASON_LENGTH) {
    return validationState({ reason: [`Keep it to ${MAX_REASON_LENGTH} characters or fewer.`] });
  }

  try {
    await api(`/api/v1/admin/reviews/${id}/${step}`, {
      method: 'POST',
      // Publishing has no reason; the other two do.
      json: reviewStepNeedsReason(step) ? { version, reason } : { version },
    });
  } catch (error) {
    return failureState(error);
  }

  const query = new URLSearchParams({ status: parseReviewStatus(text(form, 'view')), done: step });
  redirect(`/reviews?${query.toString()}`);
}
