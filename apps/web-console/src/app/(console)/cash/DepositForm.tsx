'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { FormState } from '@/lib/formState';

import { recordDeposit } from './actions';

/** One driver's "cash received" form. The amount starts as everything they hold, which is what usually happens. */
export function DepositForm({ driverUserId, held }: { driverUserId: string; held: number }) {
  const [state, action] = useActionState<FormState, FormData>(recordDeposit, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <form action={action} className="mt-4 flex flex-col gap-3 border-t border-line pt-4" noValidate>
      <input type="hidden" name="driverUserId" value={driverUserId} />
      {state.status === 'error' && state.message ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <div className="grid gap-3 sm:grid-cols-3">
        <TextField
          id={`amount-${driverUserId}`}
          name="amount"
          label="Cash received"
          inputMode="decimal"
          defaultValue={held.toFixed(2)}
          errors={errors.amount}
        />
        <TextField
          id={`reference-${driverUserId}`}
          name="reference"
          label="Receipt or bank reference"
          maxLength={100}
          errors={errors.reference}
        />
        <TextField
          id={`note-${driverUserId}`}
          name="note"
          label="Note"
          maxLength={500}
          errors={errors.note}
        />
      </div>
      <div>
        <SubmitButton variant="secondary" pendingLabel="Recording…">
          Record cash received
        </SubmitButton>
      </div>
    </form>
  );
}
