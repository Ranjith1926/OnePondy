import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, linkButton, PageHeader } from '@/components/Display';
import { partnerApi } from '@/lib/api';
import type { ListingStatus, ListingSummary } from '@/lib/apiTypes';
import { listingStatusLabels, listingTypeLabels } from '@/lib/labels';

export const metadata: Metadata = { title: 'Listings' };

const statusTone: Record<ListingStatus, string> = {
  draft: 'border border-line-strong bg-canvas text-muted',
  pending: 'bg-info-soft text-info',
  published: 'bg-ok-soft text-ok',
  suspended: 'bg-warn-soft text-warn',
};

export default async function PartnerListingsPage({ params }: { params: Promise<{ id: string }> }) {
  const { id: businessId } = await params;
  const listings = await partnerApi<ListingSummary[]>(`/api/v1/partner/businesses/${businessId}/listings`);

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href={`/partner/businesses/${businessId}`} className="font-semibold text-brand hover:underline">
          ← Business
        </Link>
      </p>
      <PageHeader
        title="Listings"
        description="What this business offers. An administrator reviews each one before it goes live."
        actions={
          <Link className={linkButton} href={`/partner/businesses/${businessId}/listings/new`}>
            Add a listing
          </Link>
        }
      />
      <Card>
        {listings.length === 0 ? (
          <p className="text-sm text-muted">No listings yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Title
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Type
                  </th>
                  <th scope="col" className="py-2">
                    Status
                  </th>
                </tr>
              </thead>
              <tbody>
                {listings.map((listing) => (
                  <tr key={listing.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4">
                      <Link
                        href={`/partner/businesses/${businessId}/listings/${listing.id}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {listing.title}
                      </Link>
                    </td>
                    <td className="py-3 pr-4">{listingTypeLabels[listing.listingType]}</td>
                    <td className="py-3">
                      <span
                        className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[listing.status]}`}
                      >
                        {listingStatusLabels[listing.status]}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  );
}
