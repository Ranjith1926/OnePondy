import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader } from '@/components/Display';
import { partnerApi } from '@/lib/api';
import type { RequesterTicketDetail } from '@/lib/apiTypes';
import { formatDateTime, formatMoney, supportStatusLabels, supportTopicLabels } from '@/lib/labels';

import { ReplyForm } from './ReplyForm';

export const metadata: Metadata = { title: 'Support request' };

const doneMessages: Record<string, string> = {
  created: 'Your request has been sent. We will answer here.',
  reply: 'Your reply has been sent.',
};

export default async function PartnerSupportRequestPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  const { id } = await params;
  const { done } = await searchParams;
  const ticket = await partnerApi<RequesterTicketDetail>(`/api/v1/support/tickets/${id}`);
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/partner/support" className="font-semibold text-brand hover:underline">
          ← Support
        </Link>
      </p>
      <PageHeader
        title={ticket.subject}
        description={`${supportTopicLabels[ticket.topic]} · ${supportStatusLabels[ticket.status]} · reference ${ticket.reference}`}
      />

      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <div className="flex flex-col gap-6">
        {ticket.about ? (
          <Card title={ticket.about.kind === 'booking' ? 'About this booking' : 'About this order'}>
            <p className="font-semibold">{ticket.about.title}</p>
            <p className="text-sm text-muted">
              {formatMoney(ticket.about.totalAmount, ticket.about.currency)} ·{' '}
              {formatDateTime(ticket.about.when)}
            </p>
          </Card>
        ) : null}

        <Card title="Conversation">
          <ol className="flex flex-col gap-4">
            {ticket.messages.map((message) => (
              <li
                key={message.id}
                className={
                  message.fromSupport
                    ? 'border-l-2 border-brand pl-3'
                    : 'border-l-2 border-line-strong pl-3'
                }
              >
                <p className="text-xs text-muted">
                  {message.fromSupport ? 'OnePondy support' : 'You'} ·{' '}
                  {formatDateTime(message.createdAt)}
                </p>
                <p className="text-sm whitespace-pre-wrap">{message.body}</p>
              </li>
            ))}
          </ol>
        </Card>

        {ticket.canReply ? (
          <Card title={ticket.status === 'resolved' ? 'Reopen this request' : 'Reply'}>
            {ticket.status === 'resolved' ? (
              <p className="mb-3 text-sm text-muted">
                This request is resolved. If it is not, reply and we will reopen it.
              </p>
            ) : null}
            <ReplyForm id={ticket.id} />
          </Card>
        ) : (
          <Card>
            <p className="text-sm text-muted">
              This request was resolved a while ago. Please start a new one.
            </p>
          </Card>
        )}
      </div>
    </>
  );
}
