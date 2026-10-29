import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader, linkButton } from '@/components/Display';
import { SettlementBadge } from '@/components/SettlementBadge';
import { StatementItems } from '@/components/StatementItems';
import { partnerApi } from '@/lib/api';
import type { SettlementDetail } from '@/lib/apiTypes';
import { formatDate, formatDateTime } from '@/lib/labels';

export const metadata: Metadata = { title: 'Statement' };

export default async function PartnerStatementPage({
  params,
}: {
  params: Promise<{ id: string; statementId: string }>;
}) {
  const { id: businessId, statementId } = await params;
  const detail = await partnerApi<SettlementDetail>(
    `/api/v1/partner/businesses/${encodeURIComponent(businessId)}/statements/${encodeURIComponent(statementId)}`,
  );
  const { settlement } = detail;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link
          href={`/partner/businesses/${businessId}/statements`}
          className="font-semibold text-brand hover:underline"
        >
          ← Statements
        </Link>
      </p>
      <PageHeader
        title={`${formatDate(settlement.periodStart)} – ${formatDate(settlement.periodEnd)}`}
        description={
          settlement.status === 'paid'
            ? `Paid ${formatDateTime(settlement.paidAt)}. Bank reference: ${settlement.payoutReference}.`
            : 'Approved. This amount will be paid to you; the bank reference appears here once it is.'
        }
        actions={
          <>
            <SettlementBadge status={settlement.status} />
            <a
              className={linkButton}
              href={`/partner/businesses/${businessId}/statements/${statementId}/csv`}
            >
              Download CSV
            </a>
          </>
        }
      />

      <Card title="Statement">
        <StatementItems detail={detail} />
      </Card>
    </>
  );
}
