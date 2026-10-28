import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { AuditEntry, Paged } from '@/lib/apiTypes';
import { auditActionLabels, formatDateTime } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

export const metadata: Metadata = { title: 'Audit log' };

const areas: Record<string, string> = {
  '': 'Everything',
  place: 'Places and photos',
  business: 'Partners',
  listing: 'Listings',
  module_flag: 'Module settings',
  app_setting: 'App version',
  staff: 'Staff access',
};

function entityLink(entry: AuditEntry) {
  if (entry.entityType === 'place') {
    return (
      <Link href={`/places/${entry.entityId}`} className="font-semibold text-brand hover:underline">
        Open place
      </Link>
    );
  }
  if (entry.entityType === 'business') {
    return (
      <Link href={`/partners/${entry.entityId}`} className="font-semibold text-brand hover:underline">
        Open partner
      </Link>
    );
  }
  if (entry.entityType === 'listing') {
    return (
      <Link href={`/listings/${entry.entityId}`} className="font-semibold text-brand hover:underline">
        Open listing
      </Link>
    );
  }
  return <span className="text-muted">{entry.entityId}</span>;
}

export default async function AuditPage({
  searchParams,
}: {
  searchParams: Promise<{ area?: string; page?: string }>;
}) {
  await requireAdministrator();
  const { area = '', page } = await searchParams;
  const pageNumber = Math.max(1, Number(page) || 1);
  const params = new URLSearchParams({ page: String(pageNumber), pageSize: '50' });
  if (area in areas && area) {
    params.set('entityType', area);
  }
  const log = await api<Paged<AuditEntry>>(`/api/v1/admin/audit?${params}`);

  return (
    <>
      <PageHeader
        title="Audit log"
        description="Every change to content, configuration and staff access. Entries cannot be edited or deleted."
      />
      <nav aria-label="Filter" className="mb-4 flex flex-wrap gap-2">
        {Object.entries(areas).map(([value, label]) => (
          <Link
            key={value}
            href={value ? `/audit?area=${value}` : '/audit'}
            aria-current={area === value ? 'page' : undefined}
            className="rounded-full border border-line-strong px-3 py-1 text-sm font-semibold aria-[current=page]:border-brand aria-[current=page]:bg-brand aria-[current=page]:text-white"
          >
            {label}
          </Link>
        ))}
      </nav>
      <Card>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-line text-xs text-muted uppercase">
              <tr>
                <th scope="col" className="py-2 pr-4">
                  When
                </th>
                <th scope="col" className="py-2 pr-4">
                  Who
                </th>
                <th scope="col" className="py-2 pr-4">
                  What
                </th>
                <th scope="col" className="py-2 pr-4">
                  Details
                </th>
                <th scope="col" className="py-2">
                  Item
                </th>
              </tr>
            </thead>
            <tbody>
              {log.items.map((entry) => (
                <tr key={entry.id} className="border-b border-line align-top last:border-b-0">
                  <td className="py-3 pr-4 whitespace-nowrap">
                    {formatDateTime(entry.occurredAt)}
                  </td>
                  <td className="py-3 pr-4">{entry.actor?.name ?? 'System'}</td>
                  <td className="py-3 pr-4">{auditActionLabels[entry.action] ?? entry.action}</td>
                  <td className="max-w-md py-3 pr-4">
                    {entry.details ? (
                      <code className="text-xs break-all text-muted">{entry.details}</code>
                    ) : (
                      '—'
                    )}
                  </td>
                  <td className="py-3">{entityLink(entry)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={log.page}
          pageSize={log.pageSize}
          totalCount={log.totalCount}
          href={(target) =>
            `/audit?${new URLSearchParams({ ...(area ? { area } : {}), page: String(target) })}`
          }
        />
      </Card>
    </>
  );
}
