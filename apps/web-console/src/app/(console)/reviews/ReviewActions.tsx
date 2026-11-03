'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { AdminReview } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { reviewStepNeedsReason, stepsForReview, type ReviewStep } from '@/lib/reviews';

import { moderateReview } from './actions';

function StepForm({
  review,
  step,
  view,
  action,
  children,
}: {
  review: AdminReview;
  step: ReviewStep;
  view: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={review.id} />
      <input type="hidden" name="step" value={step} />
      {/* The version the moderator is looking at: the API refuses if the review has moved on since. */}
      <input type="hidden" name="version" value={review.version} />
      <input type="hidden" name="view" value={view} />
      {children}
    </form>
  );
}

/** The decisions for one review. Which ones appear depends only on its status; the API makes the final decision. */
export function ReviewActions({ review, view }: { review: AdminReview; view: string }) {
  const [state, action] = useActionState<FormState, FormData>(moderateReview, { status: 'idle' });
  const errors = state.fieldErrors ?? {};
  const options = stepsForReview(review.status);

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
        reviewStepNeedsReason(option.step) ? (
          <details key={option.step} className="flex-1" open={Boolean(errors.reason)}>
            <summary className="cursor-pointer text-sm font-semibold text-brand">
              {option.label}
            </summary>
            <div className="mt-3">
              <StepForm review={review} step={option.step} view={view} action={action}>
                <TextArea
                  id={`reason-${review.id}-${option.step}`}
                  name="reason"
                  label="Reason (the author is shown it)"
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
            review={review}
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
