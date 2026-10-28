import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api, ApiError } from '@/lib/api';
import type { BusinessDetail } from '@/lib/apiTypes';
import { businessDocumentLabels, businessStatusLabels, businessTypeLabels, formatDateTime } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

import { ApprovalPanel } from './ApprovalPanel';

export const metadata: Metadata = { title: 'Partner' };

const doneMessages: Record<string, string> = {
  approve: 'Approved. The business is now live.',
  reject: 'Rejected. The partner can see your reason.',
  suspend: 'Suspended. It no longer appears in the app.',
  reinstate: 'Reinstated. It is live again.',
};

async function loadBusiness(id: string): Promise<BusinessDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await api<BusinessDetail>(`/api/v1/admin/businesses/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function PartnerPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  await requireAdministrator();
  const { id } = await params;
  const { done } = await searchParams;
  const business = await loadBusiness(id);
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/partners" className="font-semibold text-brand hover:underline">
          ← All partners
        </Link>
      </p>
      <PageHeader
        title={business.name}
        description={`${businessTypeLabels[business.businessType]} · ${businessStatusLabels[business.approvalStatus]} · last changed ${formatDateTime(business.updatedAt)}`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="flex min-w-0 flex-col gap-6">
          <Card title="Details">
            <dl className="grid gap-3 text-sm sm:grid-cols-2">
              <div>
                <dt className="font-semibold">Description</dt>
                <dd className="text-muted">{business.description ?? '—'}</dd>
              </div>
              <div>
                <dt className="font-semibold">Address</dt>
                <dd className="text-muted">{business.address ?? '—'}</dd>
              </div>
              <div>
                <dt className="font-semibold">Phone</dt>
                <dd className="text-muted">{business.phoneNumber ?? '—'}</dd>
              </div>
              <div>
                <dt className="font-semibold">Email</dt>
                <dd className="text-muted">{business.email ?? '—'}</dd>
              </div>
              <div>
                <dt className="font-semibold">Location</dt>
                <dd className="text-muted">
                  {business.latitude !== null ? `${business.latitude}, ${business.longitude}` : '—'}
                </dd>
              </div>
              <div>
                <dt className="font-semibold">GSTIN</dt>
                <dd className="text-muted">{business.gstin ?? '—'}</dd>
              </div>
              <div>
                <dt className="font-semibold">FSSAI licence</dt>
                <dd className="text-muted">{business.fssaiLicenseNumber ?? '—'}</dd>
              </div>
            </dl>
          </Card>

          <Card title="Documents">
            {business.documents.length === 0 ? (
              <p className="text-sm text-muted">No documents uploaded yet.</p>
            ) : (
              <ul className="flex flex-col gap-2 text-sm">
                {business.documents.map((document) => (
                  <li key={document.id} className="flex items-center justify-between gap-3 border-t border-line pt-2 first:border-t-0 first:pt-0">
                    <span>{businessDocumentLabels[document.kind]} · {formatDateTime(document.uploadedAt)}</span>
                    <a
                      href={`/partners/${business.id}/documents/${document.id}`}
                      target="_blank"
                      rel="noreferrer"
                      className="font-semibold text-brand hover:underline"
                    >
                      View
                    </a>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>

        <div className="flex flex-col gap-6">
          <ApprovalPanel business={business} />
        </div>
      </div>
    </>
  );
}
