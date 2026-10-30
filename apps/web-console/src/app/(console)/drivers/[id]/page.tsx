import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api, ApiError } from '@/lib/api';
import type { DriverDetail } from '@/lib/apiTypes';
import { driverStatusLabels, formatDateTime, vehicleTypeLabels } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

import { ReviewPanel } from './ReviewPanel';

export const metadata: Metadata = { title: 'Driver' };

const doneMessages: Record<string, string> = {
  approve: 'Approved. The driver can now go on duty.',
  reject: 'Rejected. The driver can see your reason.',
  suspend: 'Suspended. The driver is off duty.',
  reinstate: 'Reinstated. The driver can go on duty again.',
};

async function loadDriver(id: string): Promise<DriverDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await api<DriverDetail>(`/api/v1/admin/drivers/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function DriverPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  await requireAdministrator();
  const { id } = await params;
  const { done } = await searchParams;
  const driver = await loadDriver(id);
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/drivers" className="font-semibold text-brand hover:underline">
          ← All drivers
        </Link>
      </p>
      <PageHeader
        title={driver.fullName}
        description={`${vehicleTypeLabels[driver.vehicleType]} · ${driverStatusLabels[driver.approvalStatus]} · last changed ${formatDateTime(driver.updatedAt)}`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card title="Application">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <div>
              <dt className="font-semibold">Vehicle</dt>
              <dd className="text-muted">{vehicleTypeLabels[driver.vehicleType]}</dd>
            </div>
            <div>
              <dt className="font-semibold">Registration number</dt>
              <dd className="text-muted">{driver.vehicleNumber ?? '—'}</dd>
            </div>
            <div>
              <dt className="font-semibold">Licence number</dt>
              <dd className="text-muted">{driver.licenceNumber ?? '—'}</dd>
            </div>
            <div>
              <dt className="font-semibold">Duty</dt>
              <dd className="text-muted">
                {driver.isOnDuty && driver.onDutySince
                  ? `On duty since ${formatDateTime(driver.onDutySince)}`
                  : 'Off duty'}
              </dd>
            </div>
            <div>
              <dt className="font-semibold">Phone</dt>
              <dd className="text-muted">{driver.phoneNumber ?? '—'}</dd>
            </div>
            <div>
              <dt className="font-semibold">Email</dt>
              <dd className="text-muted">{driver.email ?? '—'}</dd>
            </div>
            <div>
              <dt className="font-semibold">Applied</dt>
              <dd className="text-muted">{formatDateTime(driver.createdAt)}</dd>
            </div>
          </dl>
          <p className="mt-4 text-sm text-muted">
            Licence and registration documents are not uploaded in the app yet; check the numbers
            against the originals before approving.
          </p>
        </Card>

        <div className="flex flex-col gap-6">
          <ReviewPanel driver={driver} />
        </div>
      </div>
    </>
  );
}
