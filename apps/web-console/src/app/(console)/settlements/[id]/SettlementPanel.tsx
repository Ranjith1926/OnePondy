'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert, Card } from '@/components/Display';
import { TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { SettlementSummary } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { changeSettlement } from '../actions';

function StepForm({
  settlement,
  step,
  action,
  children,
}: {
  settlement: SettlementSummary;
  step: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={settlement.id} />
      <input type="hidden" name="version" value={settlement.version} />
      <input type="hidden" name="step" value={step} />
      {children}
    </form>
  );
}

export function SettlementPanel({ settlement }: { settlement: SettlementSummary }) {
  const [state, action] = useActionState<FormState, FormData>(changeSettlement, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <Card title="Review" className="mb-6">
      <div className="flex flex-col gap-4 text-sm">
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}

        {settlement.status === 'draft' ? (
          <>
            <p className="text-muted">
              Check the amounts against the bookings. The partner cannot see a draft. Approving
              makes it visible to them; discarding puts its bookings back for the next statement.
            </p>
            <StepForm settlement={settlement} step="approve" action={action}>
              <div>
                <SubmitButton pendingLabel="Approving…">Approve</SubmitButton>
              </div>
            </StepForm>
            <StepForm settlement={settlement} step="discard" action={action}>
              <div>
                <SubmitButton variant="secondary" pendingLabel="Discarding…">
                  Discard draft
                </SubmitButton>
              </div>
            </StepForm>
          </>
        ) : null}

        {settlement.status === 'approved' ? (
          <StepForm settlement={settlement} step="paid" action={action}>
            <p className="text-muted">
              Transfer the net amount to the partner yourself, then record the bank reference here.
              This books the payout in the ledger and cannot be undone.
            </p>
            <TextField
              id="payoutReference"
              name="payoutReference"
              label="Bank reference (UTR)"
              maxLength={100}
              errors={errors.payoutReference}
            />
            <div>
              <SubmitButton pendingLabel="Recording…">Mark as paid</SubmitButton>
            </div>
          </StepForm>
        ) : null}

        {settlement.status === 'paid' ? (
          <p className="text-muted">Paid. Nothing more to do; the payout is in the ledger.</p>
        ) : null}
      </div>
    </Card>
  );
}
