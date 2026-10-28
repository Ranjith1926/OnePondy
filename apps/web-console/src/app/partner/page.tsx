import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, linkButton, PageHeader } from '@/components/Display';
import { partnerApi } from '@/lib/api';
import type { BusinessSummary } from '@/lib/apiTypes';
import { businessStatusLabels, businessTypeLabels, formatDateTime } from '@/lib/labels';

export const metadata: Metadata = { title: 'My businesses' };

const statusTone: Record<BusinessSummary['approvalStatus'], string> = {
  pending: 'bg-info-soft text-info',
  approved: 'bg-ok-soft text-ok',
  rejected: 'bg-danger-soft text-danger',
  suspended: 'bg-warn-soft text-warn',
};

export default async function PartnerDashboardPage() {
  const businesses = await partnerApi<BusinessSummary[]>('/api/v1/partner/businesses');

  return (
    <>
      <PageHeader
        title="My businesses"
        description="Apply to list your activity, stay, shop or service, and track its review here."
        actions={
          <Link className={linkButton} href="/partner/apply">
            Apply as a partner
          </Link>
        }
      />
      <Card>
        {businesses.length === 0 ? (
          <p className="text-sm text-muted">
            You have not applied yet.{' '}
            <Link href="/partner/apply" className="font-semibold text-brand hover:underline">
              Apply to become a partner
            </Link>
            .
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Name
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Type
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
                {businesses.map((business) => (
                  <tr key={business.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4">
                      <Link
                        href={`/partner/businesses/${business.id}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {business.name}
                      </Link>
                    </td>
                    <td className="py-3 pr-4">{businessTypeLabels[business.businessType]}</td>
                    <td className="py-3 pr-4">
                      <span
                        className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[business.approvalStatus]}`}
                      >
                        {businessStatusLabels[business.approvalStatus]}
                      </span>
                    </td>
                    <td className="py-3 whitespace-nowrap">{formatDateTime(business.updatedAt)}</td>
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
