import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import type { SettlementDetail } from '@/lib/apiTypes';

import { SettlementBadge } from './SettlementBadge';
import { StatementItems } from './StatementItems';

const detail: SettlementDetail = {
  settlement: {
    id: 's1',
    businessId: 'b1',
    businessName: 'Backwater Adventures',
    periodStart: '2026-10-01',
    periodEnd: '2026-10-07',
    grossAmount: 1000,
    commissionAmount: 50,
    refundAmount: 500,
    netAmount: 450,
    status: 'approved',
    payoutReference: null,
    createdAt: '2026-10-08T05:00:00Z',
    approvedAt: '2026-10-08T06:00:00Z',
    paidAt: null,
    version: 7,
  },
  items: [
    {
      bookingId: 'b0000000-0000-4000-8000-000000000001',
      listingTitle: 'Sunset Kayak Tour',
      serviceStartAt: '2026-10-03T06:00:00Z',
      kind: 'sale',
      grossAmount: 1000,
      commissionAmount: 100,
      partnerAmount: 900,
    },
    {
      bookingId: 'b0000000-0000-4000-8000-000000000001',
      listingTitle: 'Sunset Kayak Tour',
      serviceStartAt: '2026-10-03T06:00:00Z',
      kind: 'refund',
      grossAmount: 500,
      commissionAmount: 50,
      partnerAmount: -450,
    },
  ],
};

describe('<StatementItems />', () => {
  it('shows the totals, ending with what is payable', () => {
    render(<StatementItems detail={detail} />);

    expect(screen.getByText('Gross sales').nextSibling).toHaveTextContent(/1,000/);
    expect(screen.getByText('Commission', { selector: 'dt' }).nextSibling).toHaveTextContent(/50/);
    expect(screen.getByText('Refunds to customers').nextSibling).toHaveTextContent(/500/);
    expect(screen.getByText('Net payable').nextSibling).toHaveTextContent(/450/);
  });

  it('lists a sale and its refund as two rows for the same booking', () => {
    render(<StatementItems detail={detail} />);

    const rows = screen.getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText('Sale')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Refund')).toBeInTheDocument();
    expect(rows[1]).toHaveTextContent(/-₹\s?450|₹-450|−₹\s?450/);
  });
});

describe('<SettlementBadge />', () => {
  it.each([
    ['draft', 'Draft'],
    ['approved', 'Approved'],
    ['paid', 'Paid'],
  ] as const)('labels a %s statement', (status, label) => {
    render(<SettlementBadge status={status} />);

    expect(screen.getByText(label)).toBeInTheDocument();
  });
});
