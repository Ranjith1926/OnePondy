import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api, ApiError } from '@/lib/api';
import type { SafetyDetail, SafetyPerson } from '@/lib/apiTypes';
import {
  formatDateTime,
  rideVehicleClassLabels,
  safetyCategoryLabels,
  safetyKindLabels,
  safetyStatusLabels,
} from '@/lib/labels';
import { mapLink } from '@/lib/safety';
import { requireSupport } from '@/lib/session';

import { SafetyActions } from './SafetyActions';

export const metadata: Metadata = { title: 'Safety report' };

const doneMessages: Record<string, string> = {
  acknowledge: 'You have taken this report.',
  resolve: 'Report closed.',
};

async function load(id: string): Promise<SafetyDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await api<SafetyDetail>(`/api/v1/admin/ride-safety/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

function Person({ title, person }: { title: string; person: SafetyPerson | null }) {
  return (
    <Card title={title}>
      {person ? (
        <dl className="grid gap-2 text-sm">
          <div>
            <dt className="font-semibold">Name</dt>
            <dd className="text-muted">{person.name ?? '—'}</dd>
          </div>
          <div>
            <dt className="font-semibold">Phone</dt>
            <dd className="text-muted">{person.phoneNumber ?? '—'}</dd>
          </div>
          <div>
            <dt className="font-semibold">Email</dt>
            <dd className="text-muted">{person.email ?? '—'}</dd>
          </div>
        </dl>
      ) : (
        <p className="text-sm text-muted">No driver had taken the ride.</p>
      )}
    </Card>
  );
}

export default async function SafetyReportPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  await requireSupport();
  const { id } = await params;
  const { done } = await searchParams;
  const report = await load(id);
  const notice = done ? doneMessages[done] : undefined;
  const map = mapLink(report.latitude, report.longitude);

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/safety" className="font-semibold text-brand hover:underline">
          ← Ride safety
        </Link>
      </p>
      <PageHeader
        title={`${safetyKindLabels[report.kind]}: ${safetyCategoryLabels[report.category]}`}
        description={`${safetyStatusLabels[report.status]} · from the ${report.reportedBy} · ${formatDateTime(report.createdAt)}. Opening this report is recorded.`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="flex flex-col gap-6">
          <Card title="What was reported">
            {report.details ? (
              <p className="text-sm whitespace-pre-wrap">{report.details}</p>
            ) : (
              <p className="text-sm text-muted">
                The emergency button was pressed. No words were sent.
              </p>
            )}
            {map ? (
              <p className="mt-3 text-sm">
                <a
                  href={map}
                  target="_blank"
                  rel="noreferrer"
                  className="font-semibold text-brand hover:underline"
                >
                  Where they were when they pressed it
                </a>
              </p>
            ) : null}
            {report.resolutionNote ? (
              <p className="mt-3 text-sm">
                <span className="font-semibold">What was done: </span>
                {report.resolutionNote}
              </p>
            ) : null}
          </Card>

          <Card title="The ride">
            <dl className="grid gap-3 text-sm sm:grid-cols-2">
              <div>
                <dt className="font-semibold">From</dt>
                <dd className="text-muted">{report.ride.pickupLabel}</dd>
              </div>
              <div>
                <dt className="font-semibold">To</dt>
                <dd className="text-muted">{report.ride.destinationLabel}</dd>
              </div>
              <div>
                <dt className="font-semibold">Vehicle</dt>
                <dd className="text-muted">
                  {rideVehicleClassLabels[report.ride.vehicleClass]}
                  {report.ride.vehicleNumber ? ` · ${report.ride.vehicleNumber}` : ''}
                </dd>
              </div>
              <div>
                <dt className="font-semibold">Requested</dt>
                <dd className="text-muted">{formatDateTime(report.ride.requestedAt)}</dd>
              </div>
            </dl>
          </Card>

          <div className="grid gap-6 md:grid-cols-2">
            <Person title="Rider" person={report.rider} />
            <Person title="Driver" person={report.driver} />
          </div>
        </div>

        <SafetyActions id={report.id} version={report.version} status={report.status} />
      </div>
    </>
  );
}
