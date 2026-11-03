import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { PartnerReview } from '@/lib/apiTypes';

import { ReplyForm } from './ReplyForm';

vi.mock('./actions', () => ({ replyToReview: vi.fn() }));

const review: PartnerReview = {
  id: 'c0000000-0000-4000-8000-000000000001',
  kind: 'food_order',
  listingId: null,
  listingTitle: null,
  rating: 5,
  text: 'Hot and on time.',
  authorName: 'Anita',
  publishedAt: '2026-11-02T10:00:00Z',
  reply: null,
  version: 4242,
};

describe('<ReplyForm />', () => {
  it('says the reply is seen by everyone and cannot be changed, before it is sent', () => {
    render(<ReplyForm businessId="b0000000-0000-4000-8000-000000000001" review={review} />);

    expect(
      screen.getByText(/can reply once, and it cannot be changed afterwards/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Post reply' })).toBeInTheDocument();
  });

  it('sends the version of the review the owner is looking at', () => {
    const { container } = render(
      <ReplyForm businessId="b0000000-0000-4000-8000-000000000001" review={review} />,
    );

    expect(container.querySelector<HTMLInputElement>('input[name="version"]')?.value).toBe('4242');
    expect(container.querySelector<HTMLInputElement>('input[name="id"]')?.value).toBe(review.id);
  });
});
