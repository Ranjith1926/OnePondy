import { describe, expect, it } from 'vitest';

import type { ReviewKind, ReviewStatus } from './apiTypes';
import { asGuid, asVersion } from './ids';
import { reviewFlagLabels, reviewKindLabels, reviewStatusLabels } from './labels';
import {
  isReviewStep,
  parseReviewStatus,
  reviewStatusFilters,
  reviewStepNeedsReason,
  starsText,
  stepsForReview,
} from './reviews';

describe('what a moderator can do with a review', () => {
  it('can publish or reject one that is waiting, publishing being the main action', () => {
    const steps = stepsForReview('pending');

    expect(steps.map((s) => s.step)).toEqual(['publish', 'reject']);
    expect(steps.find((s) => s.step === 'publish')?.primary).toBe(true);
    expect(steps.find((s) => s.step === 'reject')?.primary).toBe(false);
  });

  it('can only take a published review down', () => {
    expect(stepsForReview('published').map((s) => s.step)).toEqual(['remove']);
  });

  it('can do nothing with a review that was rejected or taken down', () => {
    expect(stepsForReview('rejected')).toEqual([]);
    expect(stepsForReview('removed')).toEqual([]);
  });

  it('asks for a reason when rejecting or taking down, not when publishing', () => {
    expect(reviewStepNeedsReason('publish')).toBe(false);
    expect(reviewStepNeedsReason('reject')).toBe(true);
    expect(reviewStepNeedsReason('remove')).toBe(true);
  });

  it('recognises only the three decisions', () => {
    expect(isReviewStep('publish')).toBe(true);
    expect(isReviewStep('delete')).toBe(false);
    expect(isReviewStep(undefined)).toBe(false);
    expect(isReviewStep(null)).toBe(false);
  });
});

describe('the status filter', () => {
  it.each(['pending', 'published', 'rejected', 'removed'])('keeps %s', (status) => {
    expect(parseReviewStatus(status)).toBe(status);
  });

  it('falls back to the waiting queue for anything else', () => {
    expect(parseReviewStatus(undefined)).toBe('pending');
    expect(parseReviewStatus('everything')).toBe('pending');
    expect(parseReviewStatus('')).toBe('pending');
  });

  it('offers every status as a filter, and has a label for each', () => {
    const statuses: ReviewStatus[] = ['pending', 'published', 'rejected', 'removed'];
    expect([...reviewStatusFilters].sort()).toEqual([...statuses].sort());
    expect(Object.keys(reviewStatusLabels).sort()).toEqual([...statuses].sort());
  });

  it('has a label for both kinds of review', () => {
    const kinds: ReviewKind[] = ['booking', 'food_order'];
    expect(Object.keys(reviewKindLabels).sort()).toEqual([...kinds].sort());
  });

  it('describes every finding the automatic check can report', () => {
    for (const code of ['phone_number', 'email', 'link', 'blocked_word']) {
      expect(reviewFlagLabels[code]).toBeTruthy();
    }
  });
});

describe('starsText', () => {
  it.each([
    [1, '★☆☆☆☆'],
    [3, '★★★☆☆'],
    [5, '★★★★★'],
  ])('shows %i as %s', (rating, text) => {
    expect(starsText(rating)).toBe(text);
  });

  it('stays within five stars for a rating that is out of range', () => {
    expect(starsText(9)).toBe('★★★★★');
    expect(starsText(-2)).toBe('☆☆☆☆☆');
  });
});

describe('ids read from a form', () => {
  it('accepts a GUID and nothing that could change a request path', () => {
    expect(asGuid('0a1b2c3d-4e5f-4a7b-8c9d-0e1f2a3b4c5d')).toBe(
      '0a1b2c3d-4e5f-4a7b-8c9d-0e1f2a3b4c5d',
    );
    expect(asGuid('../../admin/staff')).toBeNull();
    expect(asGuid('0a1b2c3d-4e5f-4a7b-8c9d-0e1f2a3b4c5d/publish')).toBeNull();
    expect(asGuid('')).toBeNull();
    expect(asGuid(null)).toBeNull();
  });

  it('reads a version as a whole number and refuses an empty or odd one', () => {
    expect(asVersion('4242')).toBe(4242);
    expect(asVersion(' 7 ')).toBe(7);
    expect(asVersion('0')).toBe(0);
    expect(asVersion('')).toBeNull();
    expect(asVersion('-1')).toBeNull();
    expect(asVersion('1.5')).toBeNull();
    expect(asVersion('abc')).toBeNull();
    expect(asVersion(null)).toBeNull();
  });
});
