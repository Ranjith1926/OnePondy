'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { SupportStatus } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { stepsForTicket, ticketStepNeedsReason, type TicketStep } from '@/lib/support';

import { changeTicket } from '../actions';

function StepForm({
  id,
  version,
  step,
  action,
  children,
}: {
  id: string;
  version: number;
  step: TicketStep;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={id} />
      <input type="hidden" name="step" value={step} />
      {/* The version the agent is looking at: the API refuses if the request has moved on since. */}
      <input type="hidden" name="version" value={version} />
      {children}
    </form>
  );
}

/** Take, release, resolve and escalate. Which appear depends on the request and on who is looking; the API makes the final decision. */
export function TicketActions({
  id,
  version,
  status,
  assignedToUserId,
  me,
  escalated,
}: {
  id: string;
  version: number;
  status: SupportStatus;
  assignedToUserId: string | null;
  me: string;
  escalated: boolean;
}) {
  const [state, action] = useActionState<FormState, FormData>(changeTicket, { status: 'idle' });
  const errors = state.fieldErrors ?? {};
  const options = stepsForTicket(status, assignedToUserId, me, escalated);

  if (options.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap items-start gap-3">
      {state.status === 'error' && state.message && !state.fieldErrors ? (
        <div className="w-full">
          <Alert kind="error">{state.message}</Alert>
        </div>
      ) : null}

      {options.map((option) =>
        ticketStepNeedsReason(option.step) ? (
          <details key={option.step} className="flex-1" open={Boolean(errors.reason)}>
            <summary className="cursor-pointer text-sm font-semibold text-brand">
              {option.label}
            </summary>
            <div className="mt-3">
              <StepForm id={id} version={version} step={option.step} action={action}>
                <TextArea
                  id={`reason-${option.step}`}
                  name="reason"
                  label="Why is this being escalated?"
                  hint="An administrator reads this first."
                  rows={2}
                  maxLength={200}
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
          <StepForm key={option.step} id={id} version={version} step={option.step} action={action}>
            <SubmitButton
              variant={option.primary ? 'primary' : 'secondary'}
              pendingLabel={option.pendingLabel}
            >
              {option.label}
            </SubmitButton>
          </StepForm>
        ),
      )}
    </div>
  );
}
