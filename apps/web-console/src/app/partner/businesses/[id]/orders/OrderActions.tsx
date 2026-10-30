'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { PartnerOrderSummary } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { stepNeedsReason, stepsFor, type OrderStep } from '@/lib/foodOrders';

import { changeOrderStatus } from './actions';

function StepForm({
  businessId,
  order,
  step,
  view,
  action,
  children,
}: {
  businessId: string;
  order: PartnerOrderSummary;
  step: OrderStep;
  view: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="businessId" value={businessId} />
      <input type="hidden" name="id" value={order.id} />
      <input type="hidden" name="step" value={step} />
      {/* The version the restaurant is looking at: the API refuses if the order has moved on since. */}
      <input type="hidden" name="version" value={order.version} />
      <input type="hidden" name="view" value={view} />
      {children}
    </form>
  );
}

/** The buttons for one order. Which ones appear depends only on its status; the API makes the final decision. */
export function OrderActions({
  businessId,
  order,
  view,
}: {
  businessId: string;
  order: PartnerOrderSummary;
  view: string;
}) {
  const [state, action] = useActionState<FormState, FormData>(changeOrderStatus, {
    status: 'idle',
  });
  const errors = state.fieldErrors ?? {};
  const options = stepsFor(order.status);

  if (options.length === 0) {
    return null;
  }

  return (
    <div className="mt-4 flex flex-wrap items-start gap-3 border-t border-line pt-4">
      {state.status === 'error' && state.message ? (
        <div className="w-full">
          <Alert kind="error">{state.message}</Alert>
        </div>
      ) : null}

      {options.map((option) =>
        stepNeedsReason(option.step) ? (
          <details key={option.step} className="flex-1" open={Boolean(errors.reason)}>
            <summary className="cursor-pointer text-sm font-semibold text-brand">
              {option.label}
            </summary>
            <div className="mt-3">
              <StepForm
                businessId={businessId}
                order={order}
                step={option.step}
                view={view}
                action={action}
              >
                <TextArea
                  id={`reason-${option.step}-${order.id}`}
                  name="reason"
                  label="Reason (the customer is told)"
                  rows={2}
                  maxLength={500}
                  errors={errors.reason}
                />
                <div>
                  <SubmitButton variant="secondary" pendingLabel={option.pendingLabel}>
                    {option.label}
                  </SubmitButton>
                </div>
              </StepForm>
            </div>
          </details>
        ) : (
          <StepForm
            key={option.step}
            businessId={businessId}
            order={order}
            step={option.step}
            view={view}
            action={action}
          >
            <SubmitButton pendingLabel={option.pendingLabel}>{option.label}</SubmitButton>
          </StepForm>
        ),
      )}
    </div>
  );
}
