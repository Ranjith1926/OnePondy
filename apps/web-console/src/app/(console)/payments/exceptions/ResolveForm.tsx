'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { FormState } from '@/lib/formState';

import { resolveException } from '../actions';

export function ResolveForm({ id }: { id: string }) {
  const [state, action] = useActionState<FormState, FormData>(resolveException, { status: 'idle' });

  return (
    <form action={action} className="mt-3 flex flex-col gap-2" noValidate>
      <input type="hidden" name="id" value={id} />
      {state.status === 'error' && state.message ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <TextField
        id={`note-${id}`}
        name="note"
        label="What was done about it"
        hint="Recorded with your name in the audit log."
        maxLength={1000}
        errors={state.fieldErrors?.note}
      />
      <div>
        <SubmitButton variant="secondary" pendingLabel="Saving…">
          Mark resolved
        </SubmitButton>
      </div>
    </form>
  );
}
