import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader, Pagination } from '@/components/Display';
import { partnerApi } from '@/lib/api';
import type { Paged, PartnerReview } from '@/lib/apiTypes';
import { formatDateTime, reviewKindLabels } from '@/lib/labels';
import { starsText } from '@/lib/reviews';

import { ReplyForm } from './ReplyForm';

export const metadata: Metadata = { title: 'Reviews' };

const PAGE_SIZE = 20;

export default async function PartnerReviewsPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ page?: string; done?: string }>;
}) {
  const { id: businessId } = await params;
  const { page: requestedPage, done } = await searchParams;
  const page = Math.max(1, Number.parseInt(requestedPage ?? '1', 10) || 1);
  const reviews = await partnerApi<Paged<PartnerReview>>(
    `/api/v1/partner/businesses/${businessId}/reviews?page=${page}&pageSize=${PAGE_SIZE}`,
  );
  const unanswered = reviews.items.filter((review) => !review.reply).length;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link
          href={`/partner/businesses/${businessId}`}
          className="font-semibold text-brand hover:underline"
        >
          ← Business
        </Link>
      </p>
      <PageHeader
        title="Reviews"
        description="What customers wrote after a completed booking or a delivered order, once OnePondy has checked it. You can answer each review once. You cannot change or remove a review."
      />

      {done === 'reply' ? (
        <div className="mb-6">
          <Alert kind="success">Your reply is now shown under the review.</Alert>
        </div>
      ) : null}

      {reviews.items.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            No reviews yet. They appear here after customers write them and OnePondy publishes them.
          </p>
        </Card>
      ) : (
        <>
          {unanswered > 0 ? (
            <p className="mb-4 text-sm text-muted">
              {unanswered} review{unanswered === 1 ? '' : 's'} on this page without a reply.
            </p>
          ) : null}
          <div className="flex flex-col gap-4">
            {reviews.items.map((review) => (
              <Card key={review.id}>
                <p className="font-semibold">
                  <span aria-hidden="true">{starsText(review.rating)}</span>{' '}
                  <span>{review.rating} out of 5</span>
                </p>
                <p className="text-sm text-muted">
                  {reviewKindLabels[review.kind]}
                  {review.listingTitle ? ` · ${review.listingTitle}` : ''}
                </p>
                <p className="text-xs text-muted">
                  By {review.authorName} · {formatDateTime(review.publishedAt)}
                </p>
                {review.text ? (
                  <p className="mt-3 text-sm whitespace-pre-wrap">{review.text}</p>
                ) : (
                  <p className="mt-3 text-sm text-muted">No words, only a rating.</p>
                )}

                {review.reply ? (
                  <div className="mt-3 border-l-2 border-line-strong pl-3 text-sm">
                    <p className="text-xs text-muted">
                      Your reply, {formatDateTime(review.reply.repliedAt)}
                    </p>
                    <p className="whitespace-pre-wrap">{review.reply.text}</p>
                  </div>
                ) : (
                  <ReplyForm businessId={businessId} review={review} />
                )}
              </Card>
            ))}
          </div>
        </>
      )}

      <Pagination
        page={reviews.page}
        pageSize={reviews.pageSize}
        totalCount={reviews.totalCount}
        href={(target) => `/partner/businesses/${businessId}/reviews?page=${target}`}
      />
    </>
  );
}
