'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { FormState } from '@/lib/formState';

import { replyToRequest } from '../actions';

/** The owner's reply to support on one request. */
export function ReplyForm({ id }: { id: string }) {
  const [state, action] = useActionState<FormState, FormData>(replyToRequest, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={id} />
      {state.status === 'error' && state.message && !state.fieldErrors ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <TextArea
        id="reply-body"
        name="body"
        label="Your reply"
        rows={4}
        maxLength={2000}
        errors={errors.body}
      />
      <div>
        <SubmitButton pendingLabel="Sending…">Send reply</SubmitButton>
      </div>
    </form>
  );
}
