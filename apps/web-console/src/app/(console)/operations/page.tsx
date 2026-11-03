import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Badge, Card, PageHeader } from '@/components/Display';
import { api } from '@/lib/api';
import type { OperationsSnapshot } from '@/lib/apiTypes';
import { formatDateTime } from '@/lib/labels';
import { alertLink, rateLimitClassLabel, severityLabels, waitedText } from '@/lib/operations';
import { requireAdministrator } from '@/lib/session';

export const metadata: Metadata = { title: 'Operations' };

const linkClass = 'font-semibold text-brand hover:underline';

function Figure({ label, value, note }: { label: string; value: number; note?: string | null }) {
  return (
    <div>
      <dt className="text-xs text-muted uppercase">{label}</dt>
      <dd className="text-2xl font-bold">{value}</dd>
      {note ? <p className="text-xs text-muted">{note}</p> : null}
    </div>
  );
}

export default async function OperationsPage() {
  await requireAdministrator();
  const snapshot = await api<OperationsSnapshot>('/api/v1/admin/operations');
  const now = new Date(snapshot.generatedAt);
  const { reviews, support, notifications, signIn, rateLimits } = snapshot;
  const oldestReview = waitedText(reviews.oldestPendingAt, now);
  const oldestSupport = waitedText(support.oldestNeedingReplyAt, now);
  const oldestNotification = waitedText(notifications.oldestUnsentAt, now);

  return (
    <>
      <PageHeader
        title="Operations"
        description={`What needs looking at, as of ${formatDateTime(snapshot.generatedAt)}. Reload the page to refresh it.`}
      />

      <section aria-label="Alerts" className="mb-6 flex flex-col gap-3">
        {snapshot.alerts.length === 0 ? (
          <Alert kind="success">Nothing needs attention right now.</Alert>
        ) : (
          snapshot.alerts.map((alert) => {
            const link = alertLink(alert.area);
            return (
              <Alert key={alert.code} kind={alert.severity === 'critical' ? 'error' : 'warning'}>
                <span className="font-bold">{severityLabels[alert.severity]}: </span>
                {alert.message}{' '}
                {link ? (
                  <Link href={link.href} className="font-semibold underline">
                    {link.label}
                  </Link>
                ) : null}
              </Alert>
            );
          })
        )}
      </section>

      <div className="grid gap-4 md:grid-cols-2">
        <Card title="Reviews">
          <dl>
            <Figure
              label="Waiting for a moderator"
              value={reviews.pending}
              note={oldestReview ? `The oldest has waited ${oldestReview}.` : null}
            />
          </dl>
          <p className="mt-3 text-sm">
            <Link href="/reviews" className={linkClass}>
              Open the review queue
            </Link>
          </p>
        </Card>

        <Card title="Support">
          <dl className="grid grid-cols-3 gap-4">
            <Figure
              label="Need a reply"
              value={support.counts.needsReply}
              note={oldestSupport ? `Oldest ${oldestSupport}` : null}
            />
            <Figure label="Overdue" value={support.counts.overdue} />
            <Figure label="Escalated" value={support.counts.escalated} />
          </dl>
          <p className="mt-3 text-sm">
            <Link href="/support" className={linkClass}>
              Open support
            </Link>
          </p>
        </Card>

        <Card title="Orders and payments">
          <dl className="grid grid-cols-2 gap-4">
            <Figure label="Orders that look stuck" value={snapshot.foodOrdersNeedingAttention} />
            <Figure label="Payment differences" value={snapshot.openReconciliationExceptions} />
          </dl>
          <p className="mt-3 flex gap-4 text-sm">
            <Link href="/orders?attention=1" className={linkClass}>
              Orders
            </Link>
            <Link href="/payments/exceptions" className={linkClass}>
              Payment differences
            </Link>
          </p>
        </Card>

        <Card title="Notifications and sign-in">
          <dl className="grid grid-cols-3 gap-4">
            <Figure
              label="Not yet sent"
              value={notifications.unsent}
              note={oldestNotification ? `Oldest ${oldestNotification}` : null}
            />
            <Figure label="Codes, last hour" value={signIn.codesLastHour} />
            <Figure label="Codes, last 24 h" value={signIn.codesLast24Hours} />
          </dl>
          <p className="mt-3 text-sm text-muted">
            Sending notifications is {notifications.sending ? 'on' : 'switched off'}.
          </p>
        </Card>
      </div>

      <div className="mt-4">
        <Card title="Requests refused for coming too fast">
          <p className="mb-3 text-sm text-muted">
            Counted since the API last started ({formatDateTime(rateLimits.since)}). Only numbers
            are kept: never who sent them.
          </p>
          {rateLimits.classes.length === 0 ? (
            <p className="text-sm text-muted">No requests counted yet.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-line text-xs text-muted uppercase">
                  <tr>
                    <th scope="col" className="py-2 pr-4">
                      Kind of request
                    </th>
                    <th scope="col" className="py-2 pr-4">
                      Received
                    </th>
                    <th scope="col" className="py-2 pr-4">
                      Refused
                    </th>
                    <th scope="col" className="py-2">
                      Last refused
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {rateLimits.classes.map((row) => (
                    <tr key={row.name} className="border-b border-line last:border-b-0">
                      <td className="py-2 pr-4">{rateLimitClassLabel(row.name)}</td>
                      <td className="py-2 pr-4">{row.requests}</td>
                      <td className="py-2 pr-4">
                        {row.rejected > 0 ? (
                          <Badge tone="warn">{String(row.rejected)}</Badge>
                        ) : (
                          row.rejected
                        )}
                      </td>
                      <td className="py-2">{formatDateTime(row.lastRejectedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      </div>
    </>
  );
}
