import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader } from '@/components/Display';
import { partnerApi } from '@/lib/api';
import type { BusinessSummary, RequesterTicketSummary } from '@/lib/apiTypes';
import { formatDateTime, supportStatusLabels, supportTopicLabels } from '@/lib/labels';

import { NewRequestForm } from './NewRequestForm';

export const metadata: Metadata = { title: 'Support' };

export default async function PartnerSupportPage() {
  const [tickets, businesses] = await Promise.all([
    partnerApi<RequesterTicketSummary[]>('/api/v1/support/tickets'),
    partnerApi<BusinessSummary[]>('/api/v1/partner/businesses'),
  ]);

  return (
    <>
      <PageHeader
        title="Support"
        description="Ask OnePondy for help with a business, and follow the answers here. Complaints can also go to the grievance officer, named on the grievance page."
      />

      <div className="flex flex-col gap-6">
        <Card title="Your requests">
          {tickets.length === 0 ? (
            <p className="text-sm text-muted">You have not asked for help yet.</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {tickets.map((ticket) => (
                <li key={ticket.id}>
                  <Link
                    href={`/partner/support/${ticket.id}`}
                    className="font-semibold text-brand hover:underline"
                  >
                    {ticket.subject}
                  </Link>
                  <p className="text-xs text-muted">
                    {supportTopicLabels[ticket.topic]} · {supportStatusLabels[ticket.status]} ·
                    updated {formatDateTime(ticket.updatedAt)}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card title="New request">
          {businesses.length === 0 ? (
            <p className="text-sm text-muted">
              Apply as a partner first: requests are about one of your businesses.
            </p>
          ) : (
            <NewRequestForm businesses={businesses.map((b) => ({ id: b.id, name: b.name }))} />
          )}
        </Card>

        <p className="text-sm">
          <Link href="/grievance" className="font-semibold text-brand hover:underline">
            Who is the grievance officer?
          </Link>
        </p>
      </div>
    </>
  );
}
