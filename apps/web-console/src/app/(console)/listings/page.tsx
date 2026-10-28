import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { ListingStatus, ListingSummary, Paged } from '@/lib/apiTypes';
import { formatDateTime, listingStatusLabels, listingTypeLabels } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

export const metadata: Metadata = { title: 'Listings' };

const statusTone: Record<ListingStatus, string> = {
  draft: 'border border-line-strong bg-canvas text-muted',
  pending: 'bg-info-soft text-info',
  published: 'bg-ok-soft text-ok',
  suspended: 'bg-warn-soft text-warn',
};

interface Filters {
  status?: string;
  page?: string;
}

function query(filters: Filters, page?: number): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries({ ...filters, page: page?.toString() })) {
    if (value) {
      params.set(key, value);
    }
  }
  return params.toString();
}

export default async function ListingsPage({ searchParams }: { searchParams: Promise<Filters> }) {
  await requireAdministrator();
  const filters = await searchParams;
  const page = Math.max(1, Number(filters.page) || 1);
  const result = await api<Paged<ListingSummary>>(`/api/v1/admin/listings?${query(filters, page)}&pageSize=25`);

  return (
    <>
      <PageHeader
        title="Listings"
        description="Review and publish what partners offer: activities, stays, events, services and shops."
      />

      <Card className="mb-6">
        <form className="flex flex-wrap items-end gap-3" role="search">
          <div className="flex flex-col gap-1">
            <label htmlFor="status" className="text-xs font-semibold">
              Status
            </label>
            <select
              id="status"
              name="status"
              defaultValue={filters.status ?? ''}
              className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
            >
              <option value="">Any</option>
              {Object.entries(listingStatusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            className="min-h-10 rounded-lg border border-line-strong px-4 text-sm font-semibold hover:bg-canvas"
          >
            Filter
          </button>
          <Link href="/listings" className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      <Card>
        {result.items.length === 0 ? (
          <p className="text-sm text-muted">No listings match these filters.</p>
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
                  <th scope="col" className="py-2 pr-4">
                    Bookable
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Status
                  </th>
                  <th scope="col" className="py-2">
                    Last changed
                  </th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((listing) => (
                  <tr key={listing.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4">
                      <Link
                        href={`/listings/${listing.id}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {listing.title}
                      </Link>
                    </td>
                    <td className="py-3 pr-4">{listingTypeLabels[listing.listingType]}</td>
                    <td className="py-3 pr-4">{listing.isBookable ? 'Yes' : 'Discovery only'}</td>
                    <td className="py-3 pr-4">
                      <span
                        className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[listing.status]}`}
                      >
                        {listingStatusLabels[listing.status]}
                      </span>
                    </td>
                    <td className="py-3 whitespace-nowrap">{formatDateTime(listing.updatedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pagination
          page={result.page}
          pageSize={result.pageSize}
          totalCount={result.totalCount}
          href={(target) => `/listings?${query(filters, target)}`}
        />
      </Card>
    </>
  );
}
