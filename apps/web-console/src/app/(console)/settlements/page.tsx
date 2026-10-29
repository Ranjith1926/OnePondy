import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader, Pagination } from '@/components/Display';
import { SettlementBadge } from '@/components/SettlementBadge';
import { api } from '@/lib/api';
import type { BusinessSummary, Paged, SettlementSummary } from '@/lib/apiTypes';
import { formatDate, formatMoney, settlementStatusLabels } from '@/lib/labels';
import { queryString } from '@/lib/queryString';
import { requireAdministrator } from '@/lib/session';

import { DraftForm } from './DraftForm';

export const metadata: Metadata = { title: 'Statements' };

interface Filters {
  status?: string;
  page?: string;
  done?: string;
}

export default async function SettlementsPage({
  searchParams,
}: {
  searchParams: Promise<Filters>;
}) {
  await requireAdministrator();
  const filters = await searchParams;
  const page = Math.max(1, Number(filters.page) || 1);
  const [result, businesses] = await Promise.all([
    api<Paged<SettlementSummary>>(
      `/api/v1/admin/settlements?${queryString({ status: filters.status }, page)}&pageSize=25`,
    ),
    api<Paged<BusinessSummary>>('/api/v1/admin/businesses?status=approved&pageSize=100'),
  ]);

  return (
    <>
      <PageHeader
        title="Statements"
        description="What each partner is owed. Draft a statement, check it, approve it, then record the bank transfer."
      />
      {filters.done === 'discard' ? (
        <div className="mb-6">
          <Alert kind="success">
            Draft discarded. Its bookings are waiting for the next statement.
          </Alert>
        </div>
      ) : null}

      <DraftForm businesses={businesses.items.map(({ id, name }) => ({ id, name }))} />

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
              {Object.entries(settlementStatusLabels).map(([value, label]) => (
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
          <Link href="/settlements" className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      <Card>
        {result.items.length === 0 ? (
          <p className="text-sm text-muted">No statements yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Business
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Period
                  </th>
                  <th scope="col" className="py-2 pr-4 text-right">
                    Net payable
                  </th>
                  <th scope="col" className="py-2">
                    Status
                  </th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((statement) => (
                  <tr key={statement.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4">
                      <Link
                        href={`/settlements/${statement.id}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {statement.businessName}
                      </Link>
                    </td>
                    <td className="py-3 pr-4 whitespace-nowrap">
                      {formatDate(statement.periodStart)} – {formatDate(statement.periodEnd)}
                    </td>
                    <td className="py-3 pr-4 text-right whitespace-nowrap">
                      {formatMoney(statement.netAmount)}
                    </td>
                    <td className="py-3">
                      <SettlementBadge status={statement.status} />
                    </td>
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
          href={(target) => `/settlements?${queryString({ status: filters.status }, target)}`}
        />
      </Card>
    </>
  );
}
