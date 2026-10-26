import type { Metadata } from 'next';
import Link from 'next/link';

import { Badge, Card, linkButton, PageHeader, Pagination, StatusBadge } from '@/components/Display';
import { api } from '@/lib/api';
import type { Category, Paged, PlaceSummary } from '@/lib/apiTypes';
import { formatDateTime, statusLabels } from '@/lib/labels';
import { canEditContent } from '@/lib/roles';
import { requireUser } from '@/lib/session';

export const metadata: Metadata = { title: 'Places' };

interface Filters {
  status?: string;
  category?: string;
  q?: string;
  reviewDue?: string;
  published?: string;
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

export default async function PlacesPage({ searchParams }: { searchParams: Promise<Filters> }) {
  const user = await requireUser();
  const filters = await searchParams;
  const page = Math.max(1, Number(filters.page) || 1);
  // The filter names match the API's query parameters.
  const [result, categories] = await Promise.all([
    api<Paged<PlaceSummary>>(`/api/v1/admin/places?${query(filters, page)}&pageSize=25`),
    api<Category[]>('/api/v1/admin/places/categories'),
  ]);
  const categoryName = (code: string) => categories.find((c) => c.code === code)?.name ?? code;
  const control = 'rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm';

  return (
    <>
      <PageHeader
        title="Places"
        description="Draft, review and publish tourism places. Customers only see verified, published places."
        actions={
          canEditContent(user.role) ? (
            <Link className={linkButton} href="/places/new">
              Add a place
            </Link>
          ) : undefined
        }
      />

      <Card className="mb-6">
        <form className="flex flex-wrap items-end gap-3" role="search">
          <div className="flex flex-col gap-1">
            <label htmlFor="q" className="text-xs font-semibold">
              Search
            </label>
            <input
              id="q"
              name="q"
              defaultValue={filters.q}
              className={control}
              placeholder="Name"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor="status" className="text-xs font-semibold">
              Status
            </label>
            <select
              id="status"
              name="status"
              defaultValue={filters.status ?? ''}
              className={control}
            >
              <option value="">Any</option>
              {Object.entries(statusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor="category" className="text-xs font-semibold">
              Category
            </label>
            <select
              id="category"
              name="category"
              defaultValue={filters.category ?? ''}
              className={control}
            >
              <option value="">Any</option>
              {categories.map((category) => (
                <option key={category.code} value={category.code}>
                  {category.name}
                </option>
              ))}
            </select>
          </div>
          <label className="flex items-center gap-2 pb-2 text-sm">
            <input
              type="checkbox"
              name="reviewDue"
              value="true"
              defaultChecked={filters.reviewDue === 'true'}
              className="size-4"
            />
            Review due
          </label>
          <button
            type="submit"
            className="min-h-10 rounded-lg border border-line-strong px-4 text-sm font-semibold hover:bg-canvas"
          >
            Filter
          </button>
          <Link href="/places" className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      <Card>
        {result.items.length === 0 ? (
          <p className="text-sm text-muted">No places match these filters.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Name
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Category
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Status
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    In the app
                  </th>
                  <th scope="col" className="py-2">
                    Last changed
                  </th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((place) => (
                  <tr key={place.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4">
                      <Link
                        href={`/places/${place.id}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {place.name}
                      </Link>
                    </td>
                    <td className="py-3 pr-4">{categoryName(place.categoryCode)}</td>
                    <td className="py-3 pr-4">
                      <div className="flex flex-wrap gap-1">
                        <StatusBadge status={place.status} />
                        {place.isReviewDue ? <Badge tone="warn">Review due</Badge> : null}
                      </div>
                    </td>
                    <td className="py-3 pr-4">
                      {place.isPublished ? (
                        <Badge tone="ok">Published</Badge>
                      ) : (
                        <Badge tone="muted">No</Badge>
                      )}
                    </td>
                    <td className="py-3 whitespace-nowrap">{formatDateTime(place.updatedAt)}</td>
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
          href={(target) => `/places?${query(filters, target)}`}
        />
      </Card>
    </>
  );
}
