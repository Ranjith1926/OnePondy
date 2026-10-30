import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { DriverApprovalStatus, DriverSummary, Paged } from '@/lib/apiTypes';
import { driverStatusLabels, formatDateTime, vehicleTypeLabels } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

export const metadata: Metadata = { title: 'Drivers' };

const statusTone: Record<DriverApprovalStatus, string> = {
  pending: 'bg-info-soft text-info',
  approved: 'bg-ok-soft text-ok',
  rejected: 'bg-danger-soft text-danger',
  suspended: 'bg-warn-soft text-warn',
};

interface Filters {
  status?: string;
  page?: string;
}

function query(filters: Filters, page?: number): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries({ ...filters, page: page?.toString() })) {
    if (value) {
      params.set(key, value);
    }
  }
  return params.toString();
}

export default async function DriversPage({ searchParams }: { searchParams: Promise<Filters> }) {
  await requireAdministrator();
  const filters = await searchParams;
  const page = Math.max(1, Number(filters.page) || 1);
  const result = await api<Paged<DriverSummary>>(
    `/api/v1/admin/drivers?${query({ status: filters.status }, page)}&pageSize=25`,
  );

  return (
    <>
      <PageHeader
        title="Drivers"
        description="Review applications to deliver with OnePondy and manage the drivers already approved."
      />

      <Card className="mb-6">
        <form className="flex flex-wrap items-end gap-3" role="search">
          <div className="flex flex-col gap-1">
            <label htmlFor="status" className="text-xs font-semibold">
              Status
            </label>
            <select
              id="status"
              name="status"
              defaultValue={filters.status ?? ''}
              className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
            >
              <option value="">Any</option>
              {Object.entries(driverStatusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            className="min-h-10 rounded-lg border border-line-strong px-4 text-sm font-semibold hover:bg-canvas"
          >
            Filter
          </button>
          <Link href="/drivers" className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      <Card>
        {result.items.length === 0 ? (
          <p className="text-sm text-muted">No drivers match these filters.</p>
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
                        href={`/drivers/${driver.userId}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {driver.fullName}
                      </Link>
                    </td>
                    <td className="py-3 pr-4">{vehicleTypeLabels[driver.vehicleType]}</td>
                    <td className="py-3 pr-4">
                      <span
                        className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[driver.approvalStatus]}`}
                      >
                        {driverStatusLabels[driver.approvalStatus]}
                      </span>
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
          href={(target) => `/drivers?${query(filters, target)}`}
        />
      </Card>
    </>
  );
}
