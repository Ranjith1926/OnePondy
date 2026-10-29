'use client';

import { useActionState } from 'react';

import { Alert, Card } from '@/components/Display';
import { SelectField, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { FormState } from '@/lib/formState';

import { draftSettlement } from './actions';

export function DraftForm({ businesses }: { businesses: { id: string; name: string }[] }) {
  const [state, action] = useActionState<FormState, FormData>(draftSettlement, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <Card title="Draft a statement" className="mb-6">
      <form action={action} className="flex flex-col gap-4" noValidate>
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}
        <p className="text-sm text-muted">
          A statement lists the bookings that finished by the end of the period and are not on
          another statement yet, with the commission and any refunds. Amounts come from the ledger.
          Days are UTC.
        </p>
        <div className="grid gap-4 sm:grid-cols-3">
          <SelectField id="businessId" label="Business" errors={errors.businessId} defaultValue="">
            <option value="" disabled>
              Choose…
            </option>
            {businesses.map((business) => (
              <option key={business.id} value={business.id}>
                {business.name}
              </option>
            ))}
          </SelectField>
          <TextField
            id="periodStart"
            name="periodStart"
            type="date"
            label="From"
            errors={errors.periodStart}
          />
          <TextField
            id="periodEnd"
            name="periodEnd"
            type="date"
            label="To"
            hint="Only days that have finished."
            errors={errors.periodEnd}
          />
        </div>
        <div>
          <SubmitButton pendingLabel="Drafting…">Draft statement</SubmitButton>
        </div>
      </form>
    </Card>
  );
}
