import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api } from '@/lib/api';
import type { DriverCashRow } from '@/lib/apiTypes';
import { formatDateTime, formatMoney } from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

import { DepositForm } from './DepositForm';

export const metadata: Metadata = { title: 'Cash' };

export default async function CashPage({
  searchParams,
}: {
  searchParams: Promise<{ all?: string; recorded?: string }>;
}) {
  await requireAdministrator();
  const { all, recorded } = await searchParams;
  const showAll = all === '1';
  const rows = await api<DriverCashRow[]>(
    `/api/v1/admin/cash/drivers?onlyHolding=${showAll ? 'false' : 'true'}`,
  );
  const total = rows.reduce((sum, row) => sum + row.held, 0);

  return (
    <>
      <PageHeader
        title="Cash"
        description="Cash that drivers took at the door for cash on delivery orders and have not yet handed in to OnePondy. Record each hand-in here: it can never be more than the driver holds."
      />
      {recorded ? (
        <div className="mb-6">
          <Alert kind="success">
            Recorded. That driver&apos;s balance has gone down by that amount.
          </Alert>
        </div>
      ) : null}

      <Card className="mb-6">
        <p className="text-sm">
          <span className="font-semibold">{formatMoney(total)}</span> is held by{' '}
          {rows.filter((row) => row.held > 0).length} driver
          {rows.filter((row) => row.held > 0).length === 1 ? '' : 's'}.{' '}
          <Link
            href={showAll ? '/cash' : '/cash?all=1'}
            className="font-semibold text-brand hover:underline"
          >
            {showAll ? 'Only those holding cash' : 'Include drivers who hold nothing'}
          </Link>
        </p>
      </Card>

      {rows.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            {showAll ? 'No driver has taken cash yet.' : 'No driver is holding cash right now.'}
          </p>
        </Card>
      ) : (
        <div className="flex flex-col gap-4">
          {rows.map((row) => (
            <Card key={row.driverUserId}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="font-semibold">{row.driverName ?? 'Driver'}</p>
                  <p className="text-sm text-muted">{row.phoneNumber ?? 'No phone on file'}</p>
                  <p className="text-xs text-muted">
                    Last took cash {formatDateTime(row.lastCollectedAt)} · last handed in{' '}
                    {formatDateTime(row.lastDepositAt)}
                  </p>
                </div>
                <p className="text-lg font-semibold">{formatMoney(row.held)}</p>
              </div>
              {row.held > 0 ? (
                <DepositForm driverUserId={row.driverUserId} held={row.held} />
              ) : null}
            </Card>
          ))}
        </div>
      )}
    </>
  );
}
