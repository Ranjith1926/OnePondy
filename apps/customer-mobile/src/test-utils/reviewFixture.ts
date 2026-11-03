import type { MyReview, PublicReview, PublicReviewPage } from '../features/reviews/api/reviewsApi';
import { bookingId } from './bookingFixture';

export const reviewId = '9d0c2f64-7a5b-4a52-9d58-0d1d4f2d9b33';

export function aMyReview(overrides: Partial<MyReview> = {}): MyReview {
  return {
    id: reviewId,
    bookingId,
    foodOrderId: null,
    businessId: 'b0000000-0000-4000-8000-000000000001',
    businessName: 'Backwater Tours',
    rating: 4,
    text: 'Lovely morning on the water.',
    status: 'pending',
    moderationReason: null,
    reply: null,
    createdAt: '2026-11-02T10:00:00Z',
    canEdit: true,
    version: 1,
    ...overrides,
  };
}

export function aPublicReview(overrides: Partial<PublicReview> = {}): PublicReview {
  return {
    id: 'r0000000-0000-4000-8000-000000000001',
    rating: 5,
    text: 'Wonderful trip, would go again.',
    authorName: 'Anita',
    publishedAt: '2026-11-01T09:00:00Z',
    reply: null,
    ...overrides,
  };
}

export function aPublicReviewPage(overrides: Partial<PublicReviewPage> = {}): PublicReviewPage {
  const items = overrides.items ?? [aPublicReview()];
  return {
    rating: null,
    items,
    page: 1,
    pageSize: 25,
    totalCount: items.length,
    ...overrides,
  };
}
