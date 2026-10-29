import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { Paged, ReconciliationExceptionView, ReconciliationStatus } from '@/lib/apiTypes';
import {
  formatDateTime,
  formatMoney,
  reconciliationKindLabels,
  reconciliationStatusLabels,
} from '@/lib/labels';
import { queryString } from '@/lib/queryString';
import { requireAdministrator } from '@/lib/session';

import { PaymentsTabs } from '../PaymentsTabs';
import { ResolveForm } from './ResolveForm';

export const metadata: Metadata = { title: 'Reconciliation' };

const statusTone: Record<ReconciliationStatus, string> = {
  open: 'bg-warn-soft text-warn',
  resolved: 'bg-ok-soft text-ok',
  autoResolved: 'border border-line-strong bg-canvas text-muted',
};

interface Filters {
  status?: string;
  page?: string;
}

export default async function ExceptionsPage({ searchParams }: { searchParams: Promise<Filters> }) {
  await requireAdministrator();
  const raw = await searchParams;
  // Needs-attention first: that is what an administrator opens this page for.
  const status = raw.status ?? 'open';
  const page = Math.max(1, Number(raw.page) || 1);
  const result = await api<Paged<ReconciliationExceptionView>>(
    `/api/v1/admin/payments/exceptions?${queryString({ status }, page)}&pageSize=25`,
  );

  return (
    <>
      <PageHeader
        title="Reconciliation"
        description="Differences between our payment records and the gateway's. They are listed here for a person; nothing is changed silently."
      />
      <PaymentsTabs current="/payments/exceptions" />

      <Card className="mb-6">
        <form className="flex flex-wrap items-end gap-3" role="search">
          <div className="flex flex-col gap-1">
            <label htmlFor="status" className="text-xs font-semibold">
              Status
            </label>
            <select
              id="status"
              name="status"
              defaultValue={status}
              className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
            >
              {Object.entries(reconciliationStatusLabels).map(([value, label]) => (
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
          <Link href="/payments/exceptions" className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      {result.items.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            {status === 'open' ? 'Nothing needs attention.' : 'Nothing matches this filter.'}
          </p>
        </Card>
      ) : (
        <div className="flex flex-col gap-4">
          {result.items.map((item) => (
            <Card key={item.id}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="font-semibold">{reconciliationKindLabels[item.kind]}</p>
                  <p className="mt-1 text-sm">{item.detail}</p>
                  <p className="mt-1 text-xs text-muted">
                    First seen {formatDateTime(item.firstSeenAt)} · last{' '}
                    {formatDateTime(item.lastSeenAt)}
                    {item.occurrences > 1 ? ` · seen ${item.occurrences} times` : ''}
                  </p>
                  {item.providerReference ? (
                    <p className="mt-1 font-mono text-xs break-all">{item.providerReference}</p>
                  ) : null}
                  {item.expectedAmount !== null || item.actualAmount !== null ? (
                    <p className="mt-1 text-xs text-muted">
                      Ours: {item.expectedAmount === null ? '—' : formatMoney(item.expectedAmount)}{' '}
                      · Gateway: {item.actualAmount === null ? '—' : formatMoney(item.actualAmount)}
                    </p>
                  ) : null}
                </div>
                <span
                  className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[item.status]}`}
                >
                  {reconciliationStatusLabels[item.status]}
                </span>
              </div>

              {item.status === 'open' ? (
                <ResolveForm id={item.id} />
              ) : item.resolutionNote ? (
                <p className="mt-3 text-sm text-muted">
                  Resolved {formatDateTime(item.resolvedAt)}: {item.resolutionNote}
                </p>
              ) : null}
            </Card>
          ))}
        </div>
      )}
      <Pagination
        page={result.page}
        pageSize={result.pageSize}
        totalCount={result.totalCount}
        href={(target) => `/payments/exceptions?${queryString({ status }, target)}`}
      />
    </>
  );
}
