'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert, Card } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { BusinessDetail } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { changeBusinessStatus } from '../actions';

function StepForm({
  business,
  step,
  action,
  children,
}: {
  business: BusinessDetail;
  step: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={business.id} />
      <input type="hidden" name="version" value={business.version} />
      <input type="hidden" name="step" value={step} />
      {children}
    </form>
  );
}

export function ApprovalPanel({ business }: { business: BusinessDetail }) {
  const [state, action] = useActionState<FormState, FormData>(changeBusinessStatus, {
    status: 'idle',
  });
  const errors = state.fieldErrors ?? {};
  const needsFssai = business.businessType === 'restaurant' && !business.fssaiLicenseNumber;

  return (
    <Card title="Review">
      <div className="flex flex-col gap-4 text-sm">
        {business.approvalStatus === 'rejected' && business.reviewNote ? (
          <Alert kind="error">Rejected: {business.reviewNote}</Alert>
        ) : null}
        {business.approvalStatus === 'suspended' && business.reviewNote ? (
          <Alert kind="warning">Suspended: {business.reviewNote}</Alert>
        ) : null}
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}

        {business.approvalStatus === 'pending' ? (
          <>
            {needsFssai ? (
              <p className="text-muted">
                A restaurant needs an FSSAI licence number before it can be approved.
              </p>
            ) : null}
            <StepForm business={business} step="approve" action={action}>
              <SubmitButton disabled={needsFssai} pendingLabel="Approving…">
                Approve
              </SubmitButton>
            </StepForm>
            <StepForm business={business} step="reject" action={action}>
              <TextArea
                id="reason"
                label="Reason for rejecting"
                hint="Tell the partner what to fix."
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

        {business.approvalStatus === 'approved' ? (
          <StepForm business={business} step="suspend" action={action}>
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

        {business.approvalStatus === 'suspended' ? (
          <StepForm business={business} step="reinstate" action={action}>
            <SubmitButton pendingLabel="Reinstating…">Reinstate</SubmitButton>
          </StepForm>
        ) : null}

        {business.approvalStatus === 'rejected' ? (
          <p className="text-muted">
            Waiting for the partner to edit and resubmit the application.
          </p>
        ) : null}
      </div>
    </Card>
  );
}
