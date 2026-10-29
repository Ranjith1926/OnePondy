import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader } from '@/components/Display';
import { SettlementBadge } from '@/components/SettlementBadge';
import { StatementItems } from '@/components/StatementItems';
import { api } from '@/lib/api';
import type { SettlementDetail } from '@/lib/apiTypes';
import { formatDate, formatDateTime } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

import { SettlementPanel } from './SettlementPanel';

export const metadata: Metadata = { title: 'Statement' };

const doneMessages: Record<string, string> = {
  draft: 'Draft created. Check it, then approve it.',
  approve: 'Approved. The partner can now see this statement.',
  paid: 'Recorded as paid and booked in the ledger.',
};

export default async function SettlementPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  await requireAdministrator();
  const { id } = await params;
  const { done } = await searchParams;
  const detail = await api<SettlementDetail>(`/api/v1/admin/settlements/${encodeURIComponent(id)}`);
  const { settlement } = detail;
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/settlements" className="font-semibold text-brand hover:underline">
          ← Statements
        </Link>
      </p>
      <PageHeader
        title={settlement.businessName}
        description={`${formatDate(settlement.periodStart)} – ${formatDate(settlement.periodEnd)}`}
        actions={<SettlementBadge status={settlement.status} />}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <SettlementPanel key={settlement.version} settlement={settlement} />

      <Card title="Statement">
        <StatementItems detail={detail} />
        <p className="mt-4 text-xs text-muted">
          Created {formatDateTime(settlement.createdAt)}
          {settlement.approvedAt ? ` · approved ${formatDateTime(settlement.approvedAt)}` : ''}
          {settlement.paidAt
            ? ` · paid ${formatDateTime(settlement.paidAt)} (reference ${settlement.payoutReference})`
            : ''}
        </p>
      </Card>
    </>
  );
}
