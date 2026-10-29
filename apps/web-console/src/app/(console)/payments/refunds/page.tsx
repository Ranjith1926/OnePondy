import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { Paged, RefundRow, RefundStatus } from '@/lib/apiTypes';
import { formatDateTime, formatMoney, refundReasonLabels, refundStatusLabels } from '@/lib/labels';
import { queryString } from '@/lib/queryString';
import { requireAdministrator } from '@/lib/session';

import { PaymentsTabs } from '../PaymentsTabs';

export const metadata: Metadata = { title: 'Refunds' };

const statusTone: Record<RefundStatus, string> = {
  pending: 'bg-info-soft text-info',
  processed: 'bg-ok-soft text-ok',
  failed: 'bg-danger-soft text-danger',
};

interface Filters {
  status?: string;
  page?: string;
}

export default async function RefundsPage({ searchParams }: { searchParams: Promise<Filters> }) {
  await requireAdministrator();
  const filters = await searchParams;
  const page = Math.max(1, Number(filters.page) || 1);
  const result = await api<Paged<RefundRow>>(
    `/api/v1/admin/refunds?${queryString({ status: filters.status }, page)}&pageSize=25`,
  );

  return (
    <>
      <PageHeader
        title="Refunds"
        description="Money going back to customers. A refund marked Failed needs a person: see Reconciliation."
      />
      <PaymentsTabs current="/payments/refunds" />

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
              {Object.entries(refundStatusLabels).map(([value, label]) => (
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
          <Link href="/payments/refunds" className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      <Card>
        {result.items.length === 0 ? (
          <p className="text-sm text-muted">No refunds match these filters.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Requested
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Listing
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Reason
                  </th>
                  <th scope="col" className="py-2 pr-4 text-right">
                    Amount
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Status
                  </th>
                  <th scope="col" className="py-2">
                    Sent
                  </th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((refund) => (
                  <tr key={refund.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4 whitespace-nowrap">
                      {formatDateTime(refund.requestedAt)}
                    </td>
                    <td className="py-3 pr-4">
                      {refund.listingTitle}
                      <span className="block text-xs text-muted">{refund.businessName}</span>
                    </td>
                    <td className="py-3 pr-4">{refundReasonLabels[refund.reason]}</td>
                    <td className="py-3 pr-4 text-right whitespace-nowrap">
                      {formatMoney(refund.amount)}
                    </td>
                    <td className="py-3 pr-4">
                      <span
                        className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[refund.status]}`}
                      >
                        {refundStatusLabels[refund.status]}
                      </span>
                    </td>
                    <td className="py-3 whitespace-nowrap">{formatDateTime(refund.processedAt)}</td>
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
          href={(target) => `/payments/refunds?${queryString({ status: filters.status }, target)}`}
        />
      </Card>
    </>
  );
}
