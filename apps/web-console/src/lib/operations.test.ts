import { describe, expect, it } from 'vitest';

import { alertLink, rateLimitClassLabel, severityLabels, waitedText } from './operations';

describe('alertLink', () => {
  it('sends each alert to the page that deals with it', () => {
    expect(alertLink('payments')?.href).toBe('/payments/exceptions');
    expect(alertLink('support')?.href).toBe('/support?view=overdue');
    expect(alertLink('orders')?.href).toBe('/orders?attention=1');
    expect(alertLink('reviews')?.href).toBe('/reviews');
  });

  it('has nowhere to send an alert about the platform itself', () => {
    expect(alertLink('operations')).toBeNull();
    expect(alertLink('something_new')).toBeNull();
  });
});

describe('waitedText', () => {
  const now = new Date('2026-11-03T12:00:00Z');

  it('says nothing when nothing is waiting', () => {
    expect(waitedText(null, now)).toBeNull();
  });

  it('uses the largest sensible unit', () => {
    expect(waitedText('2026-11-03T11:48:00Z', now)).toBe('12 min');
    expect(waitedText('2026-11-03T07:00:00Z', now)).toBe('5 h');
    expect(waitedText('2026-10-31T12:00:00Z', now)).toBe('3 d');
  });

  it('never goes negative when the clocks disagree', () => {
    expect(waitedText('2026-11-03T12:05:00Z', now)).toBe('0 min');
  });
});

describe('labels', () => {
  it('names the severities and the request classes, and passes unknown classes through', () => {
    expect(severityLabels.critical).toBe('Act now');
    expect(rateLimitClassLabel('staff')).toBe('Console');
    expect(rateLimitClassLabel('brand_new')).toBe('brand_new');
  });
});

describe('audit log wording', () => {
  it('has plain words for what reviews, support and food orders record', async () => {
    const { auditActionLabels } = await import('./labels');
    for (const action of [
      'review.removed',
      'support_ticket.viewed',
      'support_ticket.contact_viewed',
      'food_order.driver_location_viewed',
      'driver_cash.deposit_recorded',
    ]) {
      expect(auditActionLabels[action], action).toBeTruthy();
    }
  });
});
