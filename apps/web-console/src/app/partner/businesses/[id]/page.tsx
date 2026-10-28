import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { ApiError, partnerApi } from '@/lib/api';
import type { BusinessDetail } from '@/lib/apiTypes';
import { businessStatusLabels } from '@/lib/labels';

import { DocumentManager } from './DocumentManager';
import { EditForm } from './EditForm';

export const metadata: Metadata = { title: 'Business' };

async function loadBusiness(id: string): Promise<BusinessDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await partnerApi<BusinessDetail>(`/api/v1/partner/businesses/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function PartnerBusinessPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const business = await loadBusiness(id);
  const canEdit = business.approvalStatus === 'pending' || business.approvalStatus === 'rejected';

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/partner" className="font-semibold text-brand hover:underline">
          ← My businesses
        </Link>
      </p>
      <PageHeader
        title={business.name}
        description={`Status: ${businessStatusLabels[business.approvalStatus]}`}
      />

      {business.approvalStatus === 'rejected' && business.reviewNote ? (
        <div className="mb-6">
          <Alert kind="error">
            Rejected: {business.reviewNote} Edit and save to send it back for review.
          </Alert>
        </div>
      ) : null}
      {business.approvalStatus === 'suspended' && business.reviewNote ? (
        <div className="mb-6">
          <Alert kind="warning">Suspended: {business.reviewNote}</Alert>
        </div>
      ) : null}

      <div className="flex flex-col gap-6">
        {canEdit ? (
          <Card title="Details">
            <EditForm key={business.version} business={business} />
          </Card>
        ) : (
          <Card title="Details">
            <p className="text-sm text-muted">
              This business is {businessStatusLabels[business.approvalStatus].toLowerCase()} and cannot be
              edited here.
            </p>
          </Card>
        )}

        <DocumentManager businessId={business.id} documents={business.documents} canUpload={canEdit} />
      </div>
    </>
  );
}
