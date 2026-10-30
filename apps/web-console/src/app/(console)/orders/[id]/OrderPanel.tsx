'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert, Card } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { AdminOrderDetail } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';

import { changeOrder } from '../actions';

function StepForm({
  orderId,
  version,
  step,
  action,
  children,
}: {
  orderId: string;
  version: number;
  step: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={orderId} />
      <input type="hidden" name="version" value={version} />
      <input type="hidden" name="step" value={step} />
      {children}
    </form>
  );
}

/** The two things an administrator may do about a stuck order. The API checks every one of them again. */
export function OrderPanel({ detail }: { detail: AdminOrderDetail }) {
  const [state, action] = useActionState<FormState, FormData>(changeOrder, { status: 'idle' });
  const errors = state.fieldErrors ?? {};
  const { order, delivery } = detail;
  const paid = order.payment !== null;

  if (!detail.canCancel) {
    return (
      <Card title="Actions">
        <p className="text-sm text-muted">This order is finished, so there is nothing to do.</p>
      </Card>
    );
  }

  return (
    <Card title="Actions">
      <div className="flex flex-col gap-6 text-sm">
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}

        {delivery?.canRelease ? (
          <StepForm orderId={order.id} version={delivery.jobVersion} step="release" action={action}>
            <p className="text-muted">
              Take the delivery from {delivery.driverName ?? 'the driver'} and offer it to the other
              drivers again. Only possible before the food is collected.
            </p>
            <SubmitButton variant="secondary" pendingLabel="Offering…">
              Offer to other drivers
            </SubmitButton>
          </StepForm>
        ) : null}

        <StepForm orderId={order.id} version={order.version} step="cancel" action={action}>
          <TextArea
            id="reason"
            label="Reason for cancelling"
            hint={
              paid
                ? 'The customer sees this, and is refunded in full.'
                : 'The customer sees this. Nothing was paid, so nothing is refunded.'
            }
            rows={2}
            maxLength={500}
            errors={errors.reason}
          />
          <SubmitButton variant="secondary" pendingLabel="Cancelling…">
            Cancel this order
          </SubmitButton>
        </StepForm>
      </div>
    </Card>
  );
}
