import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api } from '@/lib/api';
import type { AgentTicketDetail } from '@/lib/apiTypes';
import { formatDateTime, formatMoney, supportStatusLabels, supportTopicLabels } from '@/lib/labels';
import { requireSupport } from '@/lib/session';
import { ageText, escalationReasonText, flagsFor } from '@/lib/support';

import { ContactReveal } from './ContactReveal';
import { ReplyForm } from './ReplyForm';
import { TicketActions } from './TicketActions';

export const metadata: Metadata = { title: 'Support request' };

const doneMessages: Record<string, string> = {
  reply: 'Reply sent. The request now waits for the person.',
  assign: 'You have taken this request.',
  release: 'You have released this request.',
  escalate: 'Escalated. An administrator will see it first.',
};

const tone = {
  danger: 'bg-danger-soft text-danger',
  warn: 'bg-warn-soft text-warn',
} as const;

export default async function SupportRequestPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  const user = await requireSupport();
  const { id } = await params;
  const { done } = await searchParams;
  // Reading the request is itself recorded by the API: it is the person's own words an agent is looking at.
  const detail = await api<AgentTicketDetail>(`/api/v1/admin/support/tickets/${id}`);
  const { ticket, about } = detail;
  const notice = done ? doneMessages[done] : undefined;
  const now = new Date();
  const escalation = escalationReasonText(ticket.escalationReason);

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/support" className="font-semibold text-brand hover:underline">
          ← Support
        </Link>
      </p>
      <PageHeader
        title={ticket.subject}
        description={`${ticket.requesterName}${
          ticket.requesterKind === 'partner'
            ? ` (business owner${ticket.businessName ? `, ${ticket.businessName}` : ''})`
            : ''
        } · ${supportTopicLabels[ticket.topic]} · #${ticket.reference} · opened ${ageText(ticket.createdAt, now)} ago`}
      />

      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <div className="mb-6 flex flex-wrap items-center gap-2">
        <span className="rounded-full border border-line-strong bg-canvas px-3 py-1 text-xs font-semibold text-muted">
          {supportStatusLabels[ticket.status]}
        </span>
        {flagsFor(ticket).map((flag) => (
          <span
            key={flag.label}
            className={`rounded-full px-3 py-1 text-xs font-semibold ${tone[flag.tone]}`}
          >
            {flag.label}
          </span>
        ))}
        <span className="text-sm text-muted">
          {ticket.assignedToUserId === null
            ? 'Unassigned'
            : ticket.assignedToUserId === user.id
              ? 'Assigned to you'
              : 'Assigned to another agent'}
        </span>
      </div>

      {escalation ? (
        <div className="mb-6">
          <Alert kind="warning">Escalated: {escalation}</Alert>
        </div>
      ) : null}

      <div className="flex flex-col gap-6">
        {about ? (
          <Card title={about.kind === 'booking' ? 'About this booking' : 'About this order'}>
            <p className="font-semibold">{about.title}</p>
            <p className="text-sm text-muted">
              {about.status} · {formatMoney(about.totalAmount, about.currency)} ·{' '}
              {formatDateTime(about.when)}
            </p>
          </Card>
        ) : null}

        <Card title="Conversation">
          <ol className="flex flex-col gap-4">
            {detail.messages.map((message) => (
              <li
                key={message.id}
                className={
                  message.kind === 'staff'
                    ? 'border-l-2 border-brand pl-3'
                    : 'border-l-2 border-line-strong pl-3'
                }
              >
                <p className="text-xs text-muted">
                  {message.kind === 'staff'
                    ? `${message.authorName} (support)`
                    : message.authorName}{' '}
                  · {formatDateTime(message.createdAt)}
                </p>
                <p className="text-sm whitespace-pre-wrap">{message.body}</p>
              </li>
            ))}
          </ol>
        </Card>

        {ticket.status === 'resolved' ? (
          <Card>
            <p className="text-sm text-muted">
              Resolved {formatDateTime(detail.resolvedAt)}. If the person writes again within the
              reopen window, it returns to the queue.
            </p>
          </Card>
        ) : (
          <>
            <Card title="Answer">
              <ReplyForm id={ticket.id} version={detail.version} />
            </Card>
            <Card title="Work on this request">
              <TicketActions
                id={ticket.id}
                version={detail.version}
                status={ticket.status}
                assignedToUserId={ticket.assignedToUserId}
                me={user.id}
                escalated={ticket.escalated}
              />
            </Card>
          </>
        )}

        <Card title="Contact details">
          <ContactReveal id={ticket.id} />
        </Card>
      </div>
    </>
  );
}
