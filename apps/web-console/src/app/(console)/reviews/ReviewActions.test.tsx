import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { AdminReview, ReviewStatus } from '@/lib/apiTypes';

import { ReviewActions } from './ReviewActions';

// The server action reaches the API and the session; these tests are about what the buttons offer.
vi.mock('./actions', () => ({ moderateReview: vi.fn() }));

function aReview(status: ReviewStatus): AdminReview {
  return {
    id: 'c0000000-0000-4000-8000-000000000001',
    kind: 'booking',
    businessId: 'b0000000-0000-4000-8000-000000000001',
    businessName: 'Backwater Tours',
    listingId: 'l0000000-0000-4000-8000-000000000001',
    listingTitle: 'Kayaking',
    rating: 4,
    text: 'Lovely morning.',
    authorName: 'Anita',
    status,
    flags: [],
    moderationReason: null,
    createdAt: '2026-11-02T10:00:00Z',
    editedAt: null,
    reply: null,
    version: 4242,
  };
}

const show = (status: ReviewStatus, view = 'pending') =>
  render(<ReviewActions review={aReview(status)} view={view} />);

const hidden = (container: HTMLElement, name: string) =>
  container.querySelector<HTMLInputElement>(`input[type="hidden"][name="${name}"]`)?.value;

describe('<ReviewActions />', () => {
  it('offers Publish and Reject for a review that is waiting', () => {
    show('pending');

    expect(screen.getByRole('button', { name: 'Publish' })).toBeInTheDocument();
    // "Reject" is both the heading that opens the reason form and its submit button.
    expect(screen.getAllByText('Reject')).toHaveLength(2);
  });

  it('asks for a reason before rejecting, and says the author is shown it', () => {
    show('pending');

    expect(screen.getByLabelText('Reason (the author is shown it)')).toBeInTheDocument();
  });

  it('offers only Take down for a published review', () => {
    show('published');

    expect(screen.queryByRole('button', { name: 'Publish' })).not.toBeInTheDocument();
    expect(screen.getAllByText('Take down')).toHaveLength(2);
  });

  it.each(['rejected', 'removed'] as const)('offers nothing for a review that is %s', (status) => {
    const { container } = show(status);

    expect(container).toBeEmptyDOMElement();
  });

  it('sends the version it was looking at and the filter it came from', () => {
    const { container } = show('pending', 'pending');

    expect(hidden(container, 'version')).toBe('4242');
    expect(hidden(container, 'id')).toBe('c0000000-0000-4000-8000-000000000001');
    expect(hidden(container, 'view')).toBe('pending');
  });
});
