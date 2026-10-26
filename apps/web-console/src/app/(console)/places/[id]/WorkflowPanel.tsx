'use client';

import { useActionState, type ReactNode } from 'react';

import { Alert, Card, StatusBadge } from '@/components/Display';
import { TextArea } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { PlaceDetail } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { formatDateTime } from '@/lib/labels';

import { changeStatus } from '../actions';

function StepForm({
  place,
  step,
  action,
  children,
}: {
  place: PlaceDetail;
  step: string;
  action: (form: FormData) => void;
  children: ReactNode;
}) {
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="id" value={place.id} />
      <input type="hidden" name="version" value={place.version} />
      <input type="hidden" name="step" value={step} />
      {children}
    </form>
  );
}

export function WorkflowPanel({ place, canEdit }: { place: PlaceDetail; canEdit: boolean }) {
  const [state, action] = useActionState<FormState, FormData>(changeStatus, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <Card title="Review and publishing">
      <div className="flex flex-col gap-4 text-sm">
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge status={place.status} />
          <span className="font-semibold">
            {place.isPublished ? 'Live in the app' : 'Not in the app'}
          </span>
        </div>

        {place.verifiedAt ? (
          <p className="text-muted">
            Verified by {place.verifiedBy?.name} on {formatDateTime(place.verifiedAt)}.
            <br />
            Source: {place.verificationSource}
          </p>
        ) : null}
        {place.status === 'rejected' && place.reviewNote ? (
          <Alert kind="error">Rejected: {place.reviewNote}</Alert>
        ) : null}
        {place.isReviewDue ? (
          <Alert kind="warning">
            Last reviewed {formatDateTime(place.lastReviewedAt)}. Check that the details are still
            correct.
          </Alert>
        ) : null}
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}

        {!canEdit ? (
          <p className="text-muted">Your role can view this place but not change it.</p>
        ) : null}

        {canEdit && (place.status === 'draft' || place.status === 'rejected') ? (
          <StepForm place={place} step="submit" action={action}>
            {!place.isReadyForReview ? (
              <p className="text-muted">
                Add a description and a location before submitting for review.
              </p>
            ) : null}
            <SubmitButton disabled={!place.isReadyForReview} pendingLabel="Submitting…">
              Submit for review
            </SubmitButton>
          </StepForm>
        ) : null}

        {canEdit && place.status === 'pending' ? (
          <>
            <StepForm place={place} step="verify" action={action}>
              <TextArea
                id="source"
                label="How did you verify this?"
                hint="For example: Site visit on 20 Oct 2026, or the official website address."
                rows={2}
                maxLength={500}
                errors={errors.source}
              />
              <SubmitButton pendingLabel="Verifying…">Verify</SubmitButton>
            </StepForm>
            <StepForm place={place} step="reject" action={action}>
              <TextArea
                id="reason"
                label="Reason for rejecting"
                hint="Tell the editor what to fix."
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

        {canEdit && place.status === 'verified' ? (
          <>
            <StepForm
              place={place}
              step={place.isPublished ? 'unpublish' : 'publish'}
              action={action}
            >
              <SubmitButton
                variant={place.isPublished ? 'secondary' : 'primary'}
                pendingLabel="Saving…"
              >
                {place.isPublished ? 'Unpublish' : 'Publish to the app'}
              </SubmitButton>
            </StepForm>
            <details className="rounded-lg border border-line p-3">
              <summary className="cursor-pointer font-semibold">Confirm still accurate</summary>
              <div className="mt-3">
                <StepForm place={place} step="confirm-review" action={action}>
                  <TextArea
                    id="review-source"
                    name="source"
                    label="How did you check?"
                    rows={2}
                    maxLength={500}
                    errors={errors.source}
                  />
                  <SubmitButton variant="secondary" pendingLabel="Saving…">
                    Confirm
                  </SubmitButton>
                </StepForm>
              </div>
            </details>
          </>
        ) : null}
      </div>
    </Card>
  );
}
