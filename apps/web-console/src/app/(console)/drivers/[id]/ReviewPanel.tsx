'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert, Card } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { DriverDetail } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { changeDriverStatus } from '../actions';

function StepForm({
  driver,
  step,
  action,
  children,
}: {
  driver: DriverDetail;
  step: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={driver.userId} />
      <input type="hidden" name="version" value={driver.version} />
      <input type="hidden" name="step" value={step} />
      {children}
    </form>
  );
}

export function ReviewPanel({ driver }: { driver: DriverDetail }) {
  const [state, action] = useActionState<FormState, FormData>(changeDriverStatus, {
    status: 'idle',
  });
  const errors = state.fieldErrors ?? {};

  return (
    <Card title="Review">
      <div className="flex flex-col gap-4 text-sm">
        {driver.approvalStatus === 'rejected' && driver.reviewNote ? (
          <Alert kind="error">Rejected: {driver.reviewNote}</Alert>
        ) : null}
        {driver.approvalStatus === 'suspended' && driver.reviewNote ? (
          <Alert kind="warning">Suspended: {driver.reviewNote}</Alert>
        ) : null}
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}

        {driver.approvalStatus === 'pending' ? (
          <>
            <StepForm driver={driver} step="approve" action={action}>
              <SubmitButton pendingLabel="Approving…">Approve</SubmitButton>
            </StepForm>
            <StepForm driver={driver} step="reject" action={action}>
              <TextArea
                id="reason"
                label="Reason for rejecting"
                hint="Tell the driver what to fix."
                rows={2}
                maxLength={1000}
                errors={errors.reason}
              />
              <SubmitButton variant="secondary" pendingLabel="Rejecting…">
                Reject
              </SubmitButton>
            </StepForm>
          </>
        ) : null}

        {driver.approvalStatus === 'approved' ? (
          <StepForm driver={driver} step="suspend" action={action}>
            <TextArea
              id="reason"
              label="Reason for suspending"
              hint="The driver goes off duty at once and will see this."
              rows={2}
              maxLength={1000}
              errors={errors.reason}
            />
            <SubmitButton variant="secondary" pendingLabel="Suspending…">
              Suspend
            </SubmitButton>
          </StepForm>
        ) : null}

        {driver.approvalStatus === 'suspended' ? (
          <StepForm driver={driver} step="reinstate" action={action}>
            <p className="text-muted">The driver comes back off duty and starts their own shift.</p>
            <SubmitButton pendingLabel="Reinstating…">Reinstate</SubmitButton>
          </StepForm>
        ) : null}

        {driver.approvalStatus === 'rejected' ? (
          <p className="text-muted">
            Waiting for the driver to correct and resubmit the application.
          </p>
        ) : null}
      </div>
    </Card>
  );
}
