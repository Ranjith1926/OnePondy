import { apiRequest } from '../../../api/apiClient';

// Shapes returned by /api/v1/reviews (spec §7 and Phase 10). Enums arrive in camelCase.

export type ReviewStatus = 'pending' | 'published' | 'rejected' | 'removed';

/** Shown only once a listing or restaurant has enough published reviews for an average to mean something. */
export interface RatingSummary {
  average: number;
  count: number;
}

export interface ReviewReply {
  text: string;
  repliedAt: string;
}

/** A published review as anyone sees it. The author is a first name, never a phone number or an account. */
export interface PublicReview {
  id: string;
  rating: number;
  text: string | null;
  authorName: string;
  publishedAt: string;
  reply: ReviewReply | null;
}

export interface PublicReviewPage {
  rating: RatingSummary | null;
  items: PublicReview[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/** What a customer sees of their own review, including why one was not published. */
export interface MyReview {
  id: string;
  bookingId: string | null;
  foodOrderId: string | null;
  businessId: string;
  businessName: string;
  rating: number;
  text: string | null;
  status: ReviewStatus;
  moderationReason: string | null;
  reply: ReviewReply | null;
  createdAt: string;
  /** True while the author still has their one change, inside its time limit. */
  canEdit: boolean;
  version: number;
}

/** Reviews are of a bookable listing (from its completed bookings) or of a restaurant (from its delivered orders). */
export type ReviewSubject = { listingId: string } | { restaurantId: string };

export interface CreateReviewInput {
  bookingId?: string;
  foodOrderId?: string;
  rating: number;
  text?: string;
}

export interface EditReviewInput {
  version: number;
  rating: number;
  text?: string;
}

function subjectQuery(subject: ReviewSubject): string {
  return 'listingId' in subject
    ? `listingId=${encodeURIComponent(subject.listingId)}`
    : `restaurantId=${encodeURIComponent(subject.restaurantId)}`;
}

export const reviewsApi = {
  listPublic: (subject: ReviewSubject) =>
    apiRequest<PublicReviewPage>(`/api/v1/reviews?${subjectQuery(subject)}`),

  mine: () => apiRequest<MyReview[]>('/api/v1/reviews/mine', { auth: true }),

  create: (input: CreateReviewInput) =>
    apiRequest<MyReview>('/api/v1/reviews', { method: 'POST', body: input, auth: true }),

  edit: (id: string, input: EditReviewInput) =>
    apiRequest<MyReview>(`/api/v1/reviews/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: input,
      auth: true,
    }),

  remove: (id: string) =>
    apiRequest<void>(`/api/v1/reviews/${encodeURIComponent(id)}`, { method: 'DELETE', auth: true }),
};
