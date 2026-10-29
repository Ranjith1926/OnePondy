'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert, Card } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { ListingDetail } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { changeListingStatus } from '../actions';

function StepForm({
  listing,
  step,
  action,
  children,
}: {
  listing: ListingDetail;
  step: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={listing.id} />
      <input type="hidden" name="version" value={listing.version} />
      <input type="hidden" name="step" value={step} />
      {children}
    </form>
  );
}

export function ApprovalPanel({ listing }: { listing: ListingDetail }) {
  const [state, action] = useActionState<FormState, FormData>(changeListingStatus, {
    status: 'idle',
  });
  const errors = state.fieldErrors ?? {};

  return (
    <Card title="Review">
      <div className="flex flex-col gap-4 text-sm">
        {listing.status === 'draft' && listing.reviewNote ? (
          <Alert kind="error">Sent back: {listing.reviewNote}</Alert>
        ) : null}
        {listing.status === 'suspended' && listing.reviewNote ? (
          <Alert kind="warning">Suspended: {listing.reviewNote}</Alert>
        ) : null}
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}

        {listing.status === 'draft' ? (
          <p className="text-muted">Waiting for the partner to submit this for review.</p>
        ) : null}

        {listing.status === 'pending' ? (
          <>
            <StepForm listing={listing} step="publish" action={action}>
              <SubmitButton pendingLabel="Publishing…">Publish</SubmitButton>
            </StepForm>
            <StepForm listing={listing} step="send-back" action={action}>
              <TextArea
                id="reason"
                label="Reason for sending back"
                hint="Tell the partner what to fix."
                rows={2}
                maxLength={1000}
                errors={errors.reason}
              />
              <SubmitButton variant="secondary" pendingLabel="Sending back…">
                Send back
              </SubmitButton>
            </StepForm>
          </>
        ) : null}

        {listing.status === 'published' ? (
          <StepForm listing={listing} step="suspend" action={action}>
            <TextArea
              id="reason"
              label="Reason for suspending"
              hint="Tell the partner why; they will see this."
              rows={2}
              maxLength={1000}
              errors={errors.reason}
            />
            <SubmitButton variant="secondary" pendingLabel="Suspending…">
              Suspend
            </SubmitButton>
          </StepForm>
        ) : null}

        {listing.status === 'suspended' ? (
          <StepForm listing={listing} step="reinstate" action={action}>
            <SubmitButton pendingLabel="Reinstating…">Reinstate</SubmitButton>
          </StepForm>
        ) : null}
      </div>
    </Card>
  );
}
