import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader } from '@/components/Display';
import { SettlementBadge } from '@/components/SettlementBadge';
import { partnerApi } from '@/lib/api';
import type { SettlementSummary } from '@/lib/apiTypes';
import { formatDate, formatMoney } from '@/lib/labels';

export const metadata: Metadata = { title: 'Statements' };

export default async function PartnerStatementsPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id: businessId } = await params;
  const statements = await partnerApi<SettlementSummary[]>(
    `/api/v1/partner/businesses/${encodeURIComponent(businessId)}/statements`,
  );

  return (
    <>
      <p className="mb-2 text-sm">
        <Link
          href={`/partner/businesses/${businessId}`}
          className="font-semibold text-brand hover:underline"
        >
          ← Business
        </Link>
      </p>
      <PageHeader
        title="Statements"
        description="What OnePondy owes you for completed bookings, after commission and refunds. A statement appears here once it has been approved."
      />

      <Card>
        {statements.length === 0 ? (
          <p className="text-sm text-muted">No statements yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Period
                  </th>
                  <th scope="col" className="py-2 pr-4 text-right">
                    Net payable
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Status
                  </th>
                  <th scope="col" className="py-2">
                    <span className="sr-only">Download</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {statements.map((statement) => (
                  <tr key={statement.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4 whitespace-nowrap">
                      <Link
                        href={`/partner/businesses/${businessId}/statements/${statement.id}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {formatDate(statement.periodStart)} – {formatDate(statement.periodEnd)}
                      </Link>
                    </td>
                    <td className="py-3 pr-4 text-right whitespace-nowrap">
                      {formatMoney(statement.netAmount)}
                    </td>
                    <td className="py-3 pr-4">
                      <SettlementBadge status={statement.status} />
                    </td>
                    <td className="py-3 text-right">
                      <a
                        href={`/partner/businesses/${businessId}/statements/${statement.id}/csv`}
                        className="font-semibold text-brand hover:underline"
                      >
                        CSV
                      </a>
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
