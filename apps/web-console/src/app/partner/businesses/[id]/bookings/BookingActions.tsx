'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { BookingSummary } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { changeBookingStatus } from './actions';

function StepForm({
  businessId,
  booking,
  step,
  action,
  children,
}: {
  businessId: string;
  booking: BookingSummary;
  step: string;
  action: (form: FormData) => void;
  children?: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="businessId" value={businessId} />
      <input type="hidden" name="id" value={booking.id} />
      <input type="hidden" name="step" value={step} />
      {children}
    </form>
  );
}

export function BookingActions({
  businessId,
  booking,
}: {
  businessId: string;
  booking: BookingSummary;
}) {
  const [state, action] = useActionState<FormState, FormData>(changeBookingStatus, {
    status: 'idle',
  });
  const errors = state.fieldErrors ?? {};

  if (booking.status !== 'pending' && booking.status !== 'confirmed') {
    return null;
  }

  return (
    <div className="mt-4 flex flex-wrap items-start gap-3 border-t border-line pt-4">
      {state.status === 'error' && state.message ? (
        <div className="w-full">
          <Alert kind="error">{state.message}</Alert>
        </div>
      ) : null}

      {booking.status === 'pending' ? (
        <>
          <StepForm businessId={businessId} booking={booking} step="accept" action={action}>
            <SubmitButton pendingLabel="Accepting…">Accept</SubmitButton>
          </StepForm>
          <details className="flex-1">
            <summary className="cursor-pointer text-sm font-semibold text-brand">Reject</summary>
            <div className="mt-3">
              <StepForm businessId={businessId} booking={booking} step="reject" action={action}>
                <TextArea
                  id={`reason-${booking.id}`}
                  name="reason"
                  label="Reason"
                  rows={2}
                  maxLength={500}
                  errors={errors.reason}
                />
                <div>
                  <SubmitButton variant="secondary" pendingLabel="Rejecting…">
                    Reject
                  </SubmitButton>
                </div>
              </StepForm>
            </div>
          </details>
        </>
      ) : null}

      {booking.status === 'confirmed' ? (
        <>
          <StepForm businessId={businessId} booking={booking} step="complete" action={action}>
            <SubmitButton pendingLabel="Saving…">Mark completed</SubmitButton>
          </StepForm>
          <details className="flex-1">
            <summary className="cursor-pointer text-sm font-semibold text-brand">Cancel</summary>
            <div className="mt-3">
              <StepForm businessId={businessId} booking={booking} step="cancel" action={action}>
                <TextArea
                  id={`cancel-${booking.id}`}
                  name="reason"
                  label="Reason (optional)"
                  rows={2}
                  maxLength={500}
                />
                <div>
                  <SubmitButton variant="secondary" pendingLabel="Cancelling…">
                    Cancel booking
                  </SubmitButton>
                </div>
              </StepForm>
            </div>
          </details>
        </>
      ) : null}
    </div>
  );
}
