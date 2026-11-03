'use client';

import { useActionState } from 'react';

import { Alert, Card } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { SafetyStatus } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { changeSafetyReport } from '../actions';

/** Take the report, then close it with a note. A closed report offers nothing. */
export function SafetyActions({
  id,
  version,
  status,
}: {
  id: string;
  version: number;
  status: SafetyStatus;
}) {
  const [state, action] = useActionState<FormState, FormData>(changeSafetyReport, {
    status: 'idle',
  });
  const errors = state.fieldErrors ?? {};

  if (status === 'resolved') {
    return null;
  }

  return (
    <Card title="Deal with it">
      <div className="flex flex-col gap-4 text-sm">
        {state.status === 'error' && state.message && !state.fieldErrors ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}

        {status === 'open' ? (
          <form action={action} noValidate>
            <input type="hidden" name="id" value={id} />
            <input type="hidden" name="version" value={version} />
            <input type="hidden" name="step" value="acknowledge" />
            <SubmitButton variant="secondary" pendingLabel="Taking…">
              Take it
            </SubmitButton>
          </form>
        ) : null}

        <form action={action} className="flex flex-col gap-3" noValidate>
          <input type="hidden" name="id" value={id} />
          <input type="hidden" name="version" value={version} />
          <input type="hidden" name="step" value="resolve" />
          <TextArea
            id="note"
            label="What was done"
            hint="Kept with the report for the record."
            rows={3}
            maxLength={1000}
            errors={errors.note}
          />
          <SubmitButton pendingLabel="Closing…">Close report</SubmitButton>
        </form>
      </div>
    </Card>
  );
}
