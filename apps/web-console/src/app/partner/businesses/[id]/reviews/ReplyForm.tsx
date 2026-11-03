'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { PartnerReview } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { replyToReview } from './actions';

/** One reply to one review. It can be sent once and not changed, so the form says so before it is sent. */
export function ReplyForm({ businessId, review }: { businessId: string; review: PartnerReview }) {
  const [state, action] = useActionState<FormState, FormData>(replyToReview, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <details className="mt-4 border-t border-line pt-4" open={Boolean(errors.text)}>
      <summary className="cursor-pointer text-sm font-semibold text-brand">Reply</summary>
      <form action={action} className="mt-3 flex flex-col gap-3" noValidate>
        <input type="hidden" name="businessId" value={businessId} />
        <input type="hidden" name="id" value={review.id} />
        {/* The version the owner is looking at: the API refuses if the review has changed since. */}
        <input type="hidden" name="version" value={review.version} />
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}
        <TextArea
          id={`reply-${review.id}`}
          name="text"
          label="Your reply"
          hint="Everyone who reads this review will see it. You can reply once, and it cannot be changed afterwards."
          rows={3}
          maxLength={1000}
          errors={errors.text}
        />
        <div>
          <SubmitButton pendingLabel="Sending…">Post reply</SubmitButton>
        </div>
      </form>
    </details>
  );
}
