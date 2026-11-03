'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { FormState } from '@/lib/formState';

import { replyToTicket } from '../actions';

/** Support's answer. The version is the one the agent is looking at: the API refuses if the request has moved on since. */
export function ReplyForm({ id, version }: { id: string; version: number }) {
  const [state, action] = useActionState<FormState, FormData>(replyToTicket, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={id} />
      <input type="hidden" name="version" value={version} />
      {state.status === 'error' && state.message && !state.fieldErrors ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <TextArea
        id="reply-body"
        name="body"
        label="Your reply"
        hint="The person sees this as OnePondy support, without your name."
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
