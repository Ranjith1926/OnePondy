import { describe, expect, it } from 'vitest';

import {
  formatMoney,
  paymentStatusLabels,
  reconciliationKindLabels,
  reconciliationStatusLabels,
  refundReasonLabels,
  refundStatusLabels,
  settlementStatusLabels,
} from './labels';
import { queryString } from './queryString';

describe('queryString', () => {
  it('keeps filters and the page, and leaves out anything empty', () => {
    expect(queryString({ status: 'paid', businessId: '' }, 3)).toBe('status=paid&page=3');
    expect(queryString({ status: undefined })).toBe('');
  });

  it('encodes values so a filter cannot add parameters', () => {
    expect(queryString({ status: 'a&page=9' })).toBe('status=a%26page%3D9');
  });
});

describe('formatMoney', () => {
  it('shows rupees with the Indian digit grouping', () => {
    expect(formatMoney(1234567.5)).toMatch(/₹\s?12,34,567\.50/);
  });

  it('shows another currency plainly', () => {
    expect(formatMoney(10, 'USD')).toBe('USD 10.00');
  });
});

describe('payment labels', () => {
  // The API's enums are listed here by hand: a value the console has no label for would show as blank.
  it.each([
    ['payment status', paymentStatusLabels, ['created', 'captured', 'failed', 'expired']],
    ['refund status', refundStatusLabels, ['pending', 'processed', 'failed']],
    [
      'refund reason',
      refundReasonLabels,
      ['rejected', 'partnerCancelled', 'customerCancelled', 'latePayment'],
    ],
    ['statement status', settlementStatusLabels, ['draft', 'approved', 'paid']],
    ['reconciliation status', reconciliationStatusLabels, ['open', 'resolved', 'autoResolved']],
    [
      'reconciliation kind',
      reconciliationKindLabels,
      [
        'webhookNeedsAttention',
        'webhookUnprocessed',
        'paymentAppliedWithoutWebhook',
        'gatewayPaymentUnknown',
        'paymentMissingAtGateway',
        'paymentMismatch',
        'refundAppliedWithoutWebhook',
        'refundNotSent',
        'refundStuck',
        'refundFailed',
        'refundTotalMismatch',
        'ledgerProblem',
      ],
    ],
  ])('has a label for every %s', (_name, labels, values) => {
    expect(Object.keys(labels).sort()).toEqual([...values].sort());
    for (const label of Object.values(labels)) {
      expect(label.trim()).not.toBe('');
    }
  });
});
