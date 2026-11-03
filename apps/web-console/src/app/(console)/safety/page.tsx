import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Badge, Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { Paged, SafetyCounts, SafetySummary } from '@/lib/apiTypes';
import {
  formatDateTime,
  safetyCategoryLabels,
  safetyKindLabels,
  safetyStatusLabels,
} from '@/lib/labels';
import { parseSafetyView, safetyViews } from '@/lib/safety';
import { requireSupport } from '@/lib/session';

export const metadata: Metadata = { title: 'Ride safety' };

const PAGE_SIZE = 25;

export default async function SafetyQueuePage({
  searchParams,
}: {
  searchParams: Promise<{ view?: string; page?: string }>;
}) {
  await requireSupport();
  const { view: requested, page: requestedPage } = await searchParams;
  const view = parseSafetyView(requested);
  const page = Math.max(1, Number.parseInt(requestedPage ?? '1', 10) || 1);
  const [reports, counts] = await Promise.all([
    api<Paged<SafetySummary>>(
      `/api/v1/admin/ride-safety?view=${view}&page=${page}&pageSize=${PAGE_SIZE}`,
    ),
    api<SafetyCounts>('/api/v1/admin/ride-safety/counts'),
  ]);

  return (
    <>
      <PageHeader
        title="Ride safety"
        description="Emergency button presses and incident reports from rides. An emergency comes first: open it, call the people involved, and close it with a note. OnePondy is not an emergency service; if someone is in danger, they should call 112."
      />

      {counts.openSos > 0 ? (
        <div className="mb-6">
          <Alert kind="error">
            {counts.openSos} emergency {counts.openSos === 1 ? 'alert is' : 'alerts are'} waiting.
          </Alert>
        </div>
      ) : null}

      <nav aria-label="Lists" className="mb-6 flex flex-wrap gap-2">
        {safetyViews.map((option) => (
          <Link
            key={option.key}
            href={`/safety?view=${option.key}`}
            aria-current={option.key === view ? 'page' : undefined}
            className={
              option.key === view
                ? 'rounded-lg bg-brand px-3 py-1.5 text-sm font-semibold text-white'
                : 'rounded-lg border border-line-strong px-3 py-1.5 text-sm font-semibold hover:bg-canvas'
            }
          >
            {option.label}
            {option.key === 'open' && counts.openSos + counts.openIncidents > 0
              ? ` (${counts.openSos + counts.openIncidents})`
              : ''}
          </Link>
        ))}
      </nav>

      {reports.items.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            {view === 'open' ? 'Nothing is waiting.' : 'No reports in this list.'}
          </p>
        </Card>
      ) : (
        <div className="flex flex-col gap-3">
          {reports.items.map((report) => (
            <Card key={report.id}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="min-w-0">
                  <Link
                    href={`/safety/${report.id}`}
                    className="font-semibold text-brand hover:underline"
                  >
                    {safetyKindLabels[report.kind]}: {safetyCategoryLabels[report.category]}
                  </Link>
                  <p className="text-sm text-muted">
                    From the {report.reportedBy} · {formatDateTime(report.createdAt)}
                  </p>
                  {report.details ? (
                    <p className="mt-1 line-clamp-2 text-sm">{report.details}</p>
                  ) : null}
                </div>
                <div className="flex gap-2">
                  {report.kind === 'sos' && report.status !== 'resolved' ? (
                    <Badge tone="warn">Emergency</Badge>
                  ) : null}
                  <Badge tone={report.status === 'resolved' ? 'ok' : 'muted'}>
                    {safetyStatusLabels[report.status]}
                  </Badge>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Pagination
        page={reports.page}
        pageSize={reports.pageSize}
        totalCount={reports.totalCount}
        href={(target) => `/safety?view=${view}&page=${target}`}
      />
    </>
  );
}
