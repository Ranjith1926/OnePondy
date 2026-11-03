import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { AdminReview, Paged } from '@/lib/apiTypes';
import {
  formatDateTime,
  reviewFlagLabels,
  reviewKindLabels,
  reviewStatusLabels,
} from '@/lib/labels';
import { parseReviewStatus, reviewStatusFilters, starsText } from '@/lib/reviews';
import { requireEditor } from '@/lib/session';

import { ReviewActions } from './ReviewActions';

export const metadata: Metadata = { title: 'Reviews' };

const PAGE_SIZE = 20;

const doneMessages: Record<string, string> = {
  publish: 'Review published.',
  reject: 'Review rejected. The author is shown your reason.',
  remove: 'Review taken down. It no longer counts towards the rating.',
};

export default async function ReviewsPage({
  searchParams,
}: {
  searchParams: Promise<{ status?: string; page?: string; done?: string }>;
}) {
  await requireEditor();
  const { status: requested, page: requestedPage, done } = await searchParams;
  const status = parseReviewStatus(requested);
  const page = Math.max(1, Number.parseInt(requestedPage ?? '1', 10) || 1);
  const reviews = await api<Paged<AdminReview>>(
    `/api/v1/admin/reviews?status=${status}&page=${page}&pageSize=${PAGE_SIZE}`,
  );
  const notice = done ? doneMessages[done] : undefined;
  const href = (target: number) => `/reviews?status=${status}&page=${target}`;

  return (
    <>
      <PageHeader
        title="Reviews"
        description="Customers' reviews of completed bookings and delivered orders. Nothing is shown to the public until you publish it. The automatic check only points out things to look at; it never decides."
      />

      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <nav aria-label="Review status" className="mb-6 flex flex-wrap gap-2">
        {reviewStatusFilters.map((option) => (
          <Link
            key={option}
            href={`/reviews?status=${option}`}
            aria-current={option === status ? 'page' : undefined}
            className={
              option === status
                ? 'rounded-lg bg-brand px-3 py-1.5 text-sm font-semibold text-white'
                : 'rounded-lg border border-line-strong px-3 py-1.5 text-sm font-semibold hover:bg-canvas'
            }
          >
            {reviewStatusLabels[option]}
          </Link>
        ))}
      </nav>

      {reviews.items.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            {status === 'pending'
              ? 'No reviews are waiting for you.'
              : `No ${reviewStatusLabels[status].toLowerCase()} reviews.`}
          </p>
        </Card>
      ) : (
        <div className="flex flex-col gap-4">
          {reviews.items.map((review) => (
            <Card key={review.id}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="font-semibold">
                    <span aria-hidden="true">{starsText(review.rating)}</span>{' '}
                    <span>{review.rating} out of 5</span>
                  </p>
                  <p className="text-sm text-muted">
                    {review.businessName}
                    {review.listingTitle ? ` · ${review.listingTitle}` : ''} ·{' '}
                    {reviewKindLabels[review.kind]}
                  </p>
                  <p className="text-xs text-muted">
                    By {review.authorName} · written {formatDateTime(review.createdAt)}
                    {review.editedAt ? ` · changed ${formatDateTime(review.editedAt)}` : ''}
                  </p>
                </div>
                <span className="rounded-full border border-line-strong bg-canvas px-3 py-1 text-xs font-semibold text-muted">
                  {reviewStatusLabels[review.status]}
                </span>
              </div>

              {review.flags.length > 0 ? (
                <ul
                  className="mt-3 flex flex-wrap gap-2"
                  aria-label="What the automatic check found"
                >
                  {review.flags.map((flag) => (
                    <li
                      key={flag}
                      className="rounded-full bg-warn-soft px-3 py-1 text-xs font-semibold text-warn"
                    >
                      {reviewFlagLabels[flag] ?? flag}
                    </li>
                  ))}
                </ul>
              ) : null}

              {review.text ? (
                <p className="mt-3 text-sm whitespace-pre-wrap">{review.text}</p>
              ) : (
                <p className="mt-3 text-sm text-muted">No words, only a rating.</p>
              )}

              {review.moderationReason ? (
                <p className="mt-3 text-sm text-muted">Reason given: {review.moderationReason}</p>
              ) : null}

              {review.reply ? (
                <div className="mt-3 border-l-2 border-line-strong pl-3 text-sm">
                  <p className="text-xs text-muted">
                    The business replied {formatDateTime(review.reply.repliedAt)}
                  </p>
                  <p className="whitespace-pre-wrap">{review.reply.text}</p>
                </div>
              ) : null}

              <ReviewActions review={review} view={status} />
            </Card>
          ))}
        </div>
      )}

      <Pagination
        page={reviews.page}
        pageSize={reviews.pageSize}
        totalCount={reviews.totalCount}
        href={href}
      />
    </>
  );
}
