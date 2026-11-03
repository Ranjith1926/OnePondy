import type { ReviewStatus } from './apiTypes';

/** The moderators' decisions. Each is one POST to `/admin/reviews/{id}/{step}` and carries the review's version. */
export type ReviewStep = 'publish' | 'reject' | 'remove';

export const reviewSteps: readonly ReviewStep[] = ['publish', 'reject', 'remove'];

export const isReviewStep = (value: unknown): value is ReviewStep =>
  typeof value === 'string' && (reviewSteps as readonly string[]).includes(value);

/** Rejecting and taking down need a reason: the author is shown it (the API refuses without one). */
export const reviewStepNeedsReason = (step: ReviewStep): boolean => step !== 'publish';

export interface ReviewStepOption {
  step: ReviewStep;
  label: string;
  pendingLabel: string;
  /** The main thing to do, shown as a filled button; the rest are tucked away behind a heading. */
  primary: boolean;
}

/**
 * What a moderator can do with a review in each status. This only decides what to offer: the API checks every move, so an offer that is
 * out of date is refused rather than applied.
 */
export function stepsForReview(status: ReviewStatus): ReviewStepOption[] {
  switch (status) {
    case 'pending':
      return [
        { step: 'publish', label: 'Publish', pendingLabel: 'Publishing…', primary: true },
        { step: 'reject', label: 'Reject', pendingLabel: 'Rejecting…', primary: false },
      ];
    case 'published':
      return [{ step: 'remove', label: 'Take down', pendingLabel: 'Taking down…', primary: false }];
    default:
      return [];
  }
}

const statuses: readonly ReviewStatus[] = ['pending', 'published', 'rejected', 'removed'];

/** The status filter from the address bar; anything unknown means the queue of waiting reviews. */
export const parseReviewStatus = (value: string | undefined): ReviewStatus =>
  statuses.find((status) => status === value) ?? 'pending';

/** "★★★★☆" for a rating, for the eye; the number is always written beside it for everyone else. */
export function starsText(rating: number): string {
  const whole = Math.min(5, Math.max(0, Math.round(rating)));
  return '★'.repeat(whole) + '☆'.repeat(5 - whole);
}

export const reviewStatusFilters: readonly ReviewStatus[] = statuses;
