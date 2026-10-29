import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api, ApiError } from '@/lib/api';
import type { ListingDetail } from '@/lib/apiTypes';
import { listingStatusLabels, listingTypeLabels } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

import { ApprovalPanel } from './ApprovalPanel';

export const metadata: Metadata = { title: 'Listing' };

const doneMessages: Record<string, string> = {
  publish: 'Published. It now appears in the app.',
  'send-back': 'Sent back. The partner can see your reason.',
  suspend: 'Suspended. It no longer appears in the app.',
  reinstate: 'Reinstated. It is live again.',
};

async function loadListing(id: string): Promise<ListingDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await api<ListingDetail>(`/api/v1/admin/listings/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function ListingPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  await requireAdministrator();
  const { id } = await params;
  const { done } = await searchParams;
  const listing = await loadListing(id);
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/listings" className="font-semibold text-brand hover:underline">
          ← All listings
        </Link>
      </p>
      <PageHeader
        title={listing.title}
        description={`${listingTypeLabels[listing.listingType]} · ${listingStatusLabels[listing.status]}`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card title="Details">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div className="sm:col-span-2">
              <dt className="font-semibold">Description</dt>
              <dd className="text-muted">{listing.description ?? '—'}</dd>
            </div>
            <div>
              <dt className="font-semibold">Bookable</dt>
              <dd className="text-muted">{listing.isBookable ? 'Yes' : 'Discovery only'}</dd>
            </div>
            <div>
              <dt className="font-semibold">Base price</dt>
              <dd className="text-muted">
                {listing.basePrice !== null ? `${listing.currency} ${listing.basePrice}` : '—'}
              </dd>
            </div>
            {listing.cancellationPolicy ? (
              <div className="sm:col-span-2">
                <dt className="font-semibold">Cancellation policy</dt>
                <dd className="text-muted">
                  Free up to {listing.cancellationPolicy.freeCancellationHours}h before, then{' '}
                  {listing.cancellationPolicy.refundPercentage}% refund.
                  {listing.cancellationPolicy.notes ? ` ${listing.cancellationPolicy.notes}` : ''}
                </dd>
              </div>
            ) : null}
          </dl>
        </Card>

        <ApprovalPanel listing={listing} />
      </div>
    </>
  );
}
