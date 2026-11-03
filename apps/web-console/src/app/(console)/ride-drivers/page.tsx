import type { Metadata } from 'next';
import Link from 'next/link';

import { Badge, Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { DriverApprovalStatus, Paged, RideDriverSummary } from '@/lib/apiTypes';
import {
  driverStatusLabels,
  formatDate,
  formatDateTime,
  rideVehicleClassLabels,
} from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

export const metadata: Metadata = { title: 'Ride drivers' };

const statusTone: Record<DriverApprovalStatus, string> = {
  pending: 'bg-info-soft text-info',
  approved: 'bg-ok-soft text-ok',
  rejected: 'bg-danger-soft text-danger',
  suspended: 'bg-warn-soft text-warn',
};

export default async function RideDriversPage({
  searchParams,
}: {
  searchParams: Promise<{ status?: string; page?: string }>;
}) {
  await requireAdministrator();
  const { status, page: requested } = await searchParams;
  const page = Math.max(1, Number(requested) || 1);
  const params = new URLSearchParams({ page: String(page), pageSize: '25' });
  if (status && status in driverStatusLabels) {
    params.set('status', status);
  }
  const result = await api<Paged<RideDriverSummary>>(`/api/v1/admin/ride-drivers?${params}`);

  return (
    <>
      <PageHeader
        title="Ride drivers"
        description="Drivers who applied to carry passengers. Check the licence, permit and insurance against the originals before approving. A driver whose insurance has ended cannot go on duty."
      />
      <nav aria-label="Filter" className="mb-4 flex flex-wrap gap-2">
        {[['', 'Any'], ...Object.entries(driverStatusLabels)].map(([value, label]) => (
          <Link
            key={value}
            href={value ? `/ride-drivers?status=${value}` : '/ride-drivers'}
            aria-current={(status ?? '') === value ? 'page' : undefined}
            className="rounded-full border border-line-strong px-3 py-1 text-sm font-semibold aria-[current=page]:border-brand aria-[current=page]:bg-brand aria-[current=page]:text-white"
          >
            {label}
          </Link>
        ))}
      </nav>
      <Card>
        {result.items.length === 0 ? (
          <p className="text-sm text-muted">No ride drivers match.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Name
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Vehicle
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Status
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Insurance until
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Duty
                  </th>
                  <th scope="col" className="py-2">
                    Last changed
                  </th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((driver) => (
                  <tr key={driver.userId} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4">
                      <Link
                        href={`/ride-drivers/${driver.userId}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {driver.fullName}
                      </Link>
                    </td>
                    <td className="py-3 pr-4">{rideVehicleClassLabels[driver.vehicleClass]}</td>
                    <td className="py-3 pr-4">
                      <span
                        className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[driver.approvalStatus]}`}
                      >
                        {driverStatusLabels[driver.approvalStatus]}
                      </span>
                    </td>
                    <td className="py-3 pr-4 whitespace-nowrap">
                      {formatDate(driver.insuranceValidUntil)}{' '}
                      {driver.insuranceEnded ? <Badge tone="warn">Ended</Badge> : null}
                    </td>
                    <td className="py-3 pr-4">{driver.isOnDuty ? 'On duty' : 'Off duty'}</td>
                    <td className="py-3 whitespace-nowrap">{formatDateTime(driver.updatedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pagination
          page={result.page}
          pageSize={result.pageSize}
          totalCount={result.totalCount}
          href={(target) =>
            `/ride-drivers?${new URLSearchParams({ ...(status ? { status } : {}), page: String(target) })}`
          }
        />
      </Card>
    </>
  );
}
