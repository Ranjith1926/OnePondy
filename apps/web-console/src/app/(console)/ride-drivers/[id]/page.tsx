import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api, ApiError } from '@/lib/api';
import type { RideDriverDetail } from '@/lib/apiTypes';
import {
  driverStatusLabels,
  formatDate,
  formatDateTime,
  rideVehicleClassLabels,
} from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

import { ReviewPanel } from './ReviewPanel';

export const metadata: Metadata = { title: 'Ride driver' };

const doneMessages: Record<string, string> = {
  approve: 'Approved. The driver can now go on duty while their insurance is valid.',
  reject: 'Rejected. The driver can see your reason.',
  suspend:
    'Suspended. The driver is off duty and any ride they had not reached goes back to others.',
  reinstate: 'Reinstated. The driver can go on duty again.',
};

async function load(id: string): Promise<RideDriverDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await api<RideDriverDetail>(`/api/v1/admin/ride-drivers/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function RideDriverPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  await requireAdministrator();
  const { id } = await params;
  const { done } = await searchParams;
  const driver = await load(id);
  const notice = done ? doneMessages[done] : undefined;

  const rows: [string, string][] = [
    ['Vehicle', rideVehicleClassLabels[driver.vehicleClass]],
    ['Registration number', driver.vehicleNumber],
    ['Licence number', driver.licenceNumber],
    ['Permit number', driver.permitNumber],
    ['Insurance policy', driver.insurancePolicyNumber],
    [
      'Insurance valid until',
      formatDate(driver.insuranceValidUntil) + (driver.insuranceEnded ? ' (ended)' : ''),
    ],
    [
      'Duty',
      driver.isOnDuty && driver.onDutySince
        ? `On duty since ${formatDateTime(driver.onDutySince)}`
        : 'Off duty',
    ],
    ['Phone', driver.phoneNumber ?? '—'],
    ['Email', driver.email ?? '—'],
    ['Applied', formatDateTime(driver.createdAt)],
  ];

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/ride-drivers" className="font-semibold text-brand hover:underline">
          ← All ride drivers
        </Link>
      </p>
      <PageHeader
        title={driver.fullName}
        description={`${rideVehicleClassLabels[driver.vehicleClass]} · ${driverStatusLabels[driver.approvalStatus]} · last changed ${formatDateTime(driver.updatedAt)}`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}
      {driver.insuranceEnded ? (
        <div className="mb-6">
          <Alert kind="warning">
            This driver&apos;s insurance has ended, so they cannot go on duty until they send a new
            policy.
          </Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card title="Application">
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            {rows.map(([label, value]) => (
              <div key={label}>
                <dt className="font-semibold">{label}</dt>
                <dd className="text-muted">{value}</dd>
              </div>
            ))}
          </dl>
          <p className="mt-4 text-sm text-muted">
            Documents are not uploaded in the app yet; check the numbers against the originals
            before approving.
          </p>
        </Card>
        <div className="flex flex-col gap-6">
          <ReviewPanel driver={driver} />
        </div>
      </div>
    </>
  );
}
