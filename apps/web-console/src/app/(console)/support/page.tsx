import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { AgentTicketSummary, Paged, SupportCounts } from '@/lib/apiTypes';
import { supportStatusLabels, supportTopicLabels } from '@/lib/labels';
import { agentViews, ageText, flagsFor, parseAgentView, type AgentView } from '@/lib/support';
import { requireSupport } from '@/lib/session';

export const metadata: Metadata = { title: 'Support' };

const PAGE_SIZE = 25;

const doneMessages: Record<string, string> = {
  resolve: 'Request resolved. The person can reopen it by replying.',
};

const tone = {
  danger: 'bg-danger-soft text-danger',
  warn: 'bg-warn-soft text-warn',
} as const;

/** The number beside a list's name, for the lists whose size says what needs doing. */
function countFor(view: AgentView, counts: SupportCounts): number | null {
  switch (view) {
    case 'needs_reply':
      return counts.needsReply;
    case 'waiting':
      return counts.waiting;
    case 'unassigned':
      return counts.unassigned;
    case 'overdue':
      return counts.overdue;
    case 'escalated':
      return counts.escalated;
    default:
      return null;
  }
}

export default async function SupportQueuePage({
  searchParams,
}: {
  searchParams: Promise<{ view?: string; page?: string; done?: string }>;
}) {
  const user = await requireSupport();
  const { view: requested, page: requestedPage, done } = await searchParams;
  const view = parseAgentView(requested);
  const page = Math.max(1, Number.parseInt(requestedPage ?? '1', 10) || 1);
  const [tickets, counts] = await Promise.all([
    api<Paged<AgentTicketSummary>>(
      `/api/v1/admin/support/tickets?view=${view}&page=${page}&pageSize=${PAGE_SIZE}`,
    ),
    api<SupportCounts>('/api/v1/admin/support/counts'),
  ]);
  const now = new Date();
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <PageHeader
        title="Support"
        description="Requests from customers and from business owners. Open one to read it and answer; what you see is the request and the one booking or order it is about, and opening it is recorded."
      />

      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <nav aria-label="Lists" className="mb-6 flex flex-wrap gap-2">
        {agentViews.map((option) => {
          const count = countFor(option.key, counts);
          return (
            <Link
              key={option.key}
              href={`/support?view=${option.key}`}
              aria-current={option.key === view ? 'page' : undefined}
              className={
                option.key === view
                  ? 'rounded-lg bg-brand px-3 py-1.5 text-sm font-semibold text-white'
                  : 'rounded-lg border border-line-strong px-3 py-1.5 text-sm font-semibold hover:bg-canvas'
              }
            >
              {option.label}
              {count !== null && count > 0 ? ` (${count})` : ''}
            </Link>
          );
        })}
      </nav>

      {tickets.items.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            {view === 'needs_reply'
              ? 'Nothing is waiting for a reply.'
              : 'No requests in this list.'}
          </p>
        </Card>
      ) : (
        <div className="flex flex-col gap-3">
          {tickets.items.map((ticket) => (
            <Card key={ticket.id}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="min-w-0">
                  <Link
                    href={`/support/${ticket.id}`}
                    className="font-semibold text-brand hover:underline"
                  >
                    {ticket.subject}
                  </Link>
                  <p className="text-sm text-muted">
                    {ticket.requesterName}
                    {ticket.requesterKind === 'partner'
                      ? ` (business owner${ticket.businessName ? `, ${ticket.businessName}` : ''})`
                      : ''}{' '}
                    · {supportTopicLabels[ticket.topic]} · #{ticket.reference}
                  </p>
                  <p className="text-xs text-muted">
                    {supportStatusLabels[ticket.status]} · opened {ageText(ticket.createdAt, now)}{' '}
                    ago ·{' '}
                    {ticket.assignedToUserId === null
                      ? 'unassigned'
                      : ticket.assignedToUserId === user.id
                        ? 'yours'
                        : 'with another agent'}
                  </p>
                </div>
                <ul className="flex flex-wrap gap-2" aria-label="Things to notice">
                  {flagsFor(ticket).map((flag) => (
                    <li
                      key={flag.label}
                      className={`rounded-full px-3 py-1 text-xs font-semibold ${tone[flag.tone]}`}
                    >
                      {flag.label}
                    </li>
                  ))}
                </ul>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Pagination
        page={tickets.page}
        pageSize={tickets.pageSize}
        totalCount={tickets.totalCount}
        href={(target) => `/support?view=${view}&page=${target}`}
      />
    </>
  );
}
