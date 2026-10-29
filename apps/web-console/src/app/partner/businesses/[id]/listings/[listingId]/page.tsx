import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { SubmitButton } from '@/components/SubmitButton';
import { ApiError, partnerApi } from '@/lib/api';
import type { AvailabilitySlot, ListingDetail } from '@/lib/apiTypes';
import { listingStatusLabels } from '@/lib/labels';

import { submitListing } from '../actions';
import { ListingForm } from '../ListingForm';
import { SlotManager } from './SlotManager';

export const metadata: Metadata = { title: 'Listing' };

const doneMessages: Record<string, string> = {
  submit: 'Submitted for review.',
};

async function loadListing(businessId: string, id: string): Promise<ListingDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await partnerApi<ListingDetail>(
      `/api/v1/partner/businesses/${businessId}/listings/${id}`,
    );
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function PartnerListingPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string; listingId: string }>;
  searchParams: Promise<{ saved?: string; done?: string; slot?: string }>;
}) {
  const { id: businessId, listingId } = await params;
  const { saved, done, slot } = await searchParams;
  const [listing, slots] = await Promise.all([
    loadListing(businessId, listingId),
    partnerApi<AvailabilitySlot[]>(
      `/api/v1/partner/businesses/${businessId}/listings/${listingId}/slots`,
    ),
  ]);
  const canEdit = listing.status === 'draft';
  const notice = saved ? 'Changes saved.' : done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link
          href={`/partner/businesses/${businessId}/listings`}
          className="font-semibold text-brand hover:underline"
        >
          ← Listings
        </Link>
      </p>
      <PageHeader
        title={listing.title}
        description={`Status: ${listingStatusLabels[listing.status]}`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}
      {listing.status === 'draft' && listing.reviewNote ? (
        <div className="mb-6">
          <Alert kind="error">
            Sent back: {listing.reviewNote} Edit and submit again when ready.
          </Alert>
        </div>
      ) : null}
      {listing.status === 'suspended' && listing.reviewNote ? (
        <div className="mb-6">
          <Alert kind="warning">Suspended: {listing.reviewNote}</Alert>
        </div>
      ) : null}
      {slot === 'kept' ? (
        <div className="mb-6">
          <Alert kind="warning">
            That time has booking history, so it cannot be removed. It keeps its record of who
            booked it.
          </Alert>
        </div>
      ) : null}

      <div className="flex flex-col gap-6">
        <Card title="Details">
          {canEdit ? (
            <div className="flex flex-col gap-4">
              <ListingForm key={listing.version} businessId={businessId} listing={listing} />
              <form action={submitListing} className="border-t border-line pt-4">
                <input type="hidden" name="businessId" value={businessId} />
                <input type="hidden" name="id" value={listing.id} />
                <input type="hidden" name="version" value={listing.version} />
                <SubmitButton pendingLabel="Submitting…">Submit for review</SubmitButton>
              </form>
            </div>
          ) : (
            <p className="text-sm text-muted">
              This listing is {listingStatusLabels[listing.status].toLowerCase()} and cannot be
              edited here.
            </p>
          )}
        </Card>

        {listing.isBookable ? (
          <SlotManager businessId={businessId} listingId={listing.id} slots={slots} />
        ) : null}
      </div>
    </>
  );
}
