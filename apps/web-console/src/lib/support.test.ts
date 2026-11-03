import { describe, expect, it } from 'vitest';

import type { AgentTicketSummary } from './apiTypes';
import { canWorkSupport } from './roles';
import {
  agentViews,
  ageText,
  escalationReasonText,
  flagsFor,
  isTicketStep,
  parseAgentView,
  stepsForTicket,
  ticketStepNeedsReason,
} from './support';

const me = 'u-me';

function aTicket(overrides: Partial<AgentTicketSummary> = {}): AgentTicketSummary {
  return {
    id: 't1',
    reference: 'ABCD1234',
    topic: 'foodOrder',
    subject: 'Where is my order?',
    status: 'open',
    requesterKind: 'customer',
    requesterName: 'Anita',
    businessName: null,
    assignedToUserId: null,
    escalated: false,
    escalationReason: null,
    firstReplyOverdue: false,
    resolutionOverdue: false,
    createdAt: '2026-11-03T08:00:00Z',
    lastRequesterMessageAt: '2026-11-03T08:00:00Z',
    ...overrides,
  };
}

describe('who can work the support queue', () => {
  it('is administrators and support agents, and not content editors', () => {
    expect(canWorkSupport('administrator')).toBe(true);
    expect(canWorkSupport('support_agent')).toBe(true);
    expect(canWorkSupport('content_editor')).toBe(false);
  });
});

describe('the agents’ lists', () => {
  it.each(agentViews.map((view) => view.key))('keeps %s', (key) => {
    expect(parseAgentView(key)).toBe(key);
  });

  it('falls back to the requests that need a reply for anything else', () => {
    expect(parseAgentView(undefined)).toBe('needs_reply');
    expect(parseAgentView('everything')).toBe('needs_reply');
    expect(parseAgentView('')).toBe('needs_reply');
  });

  it('offers every list the API knows', () => {
    expect(agentViews.map((view) => view.key).sort()).toEqual(
      [
        'all',
        'escalated',
        'mine',
        'needs_reply',
        'overdue',
        'resolved',
        'unassigned',
        'waiting',
      ].sort(),
    );
  });
});

describe('what an agent can do with a request', () => {
  it('can take an unassigned request, resolve it or escalate it, resolving being the main action', () => {
    const steps = stepsForTicket('open', null, me, false);

    expect(steps.map((s) => s.step)).toEqual(['assign', 'resolve', 'escalate']);
    expect(steps.find((s) => s.step === 'resolve')?.primary).toBe(true);
    expect(steps.find((s) => s.step === 'assign')?.primary).toBe(false);
  });

  it('can release a request that is their own, and take one that is another agent’s', () => {
    expect(stepsForTicket('open', me, me, false).map((s) => s.step)).toContain('release');
    expect(stepsForTicket('open', me, me, false).map((s) => s.step)).not.toContain('assign');
    expect(stepsForTicket('open', 'u-other', me, false).map((s) => s.step)).toContain('assign');
  });

  it('cannot escalate a request that is already escalated', () => {
    expect(stepsForTicket('open', null, me, true).map((s) => s.step)).not.toContain('escalate');
  });

  it('can do nothing with a resolved request: the person reopens it by replying', () => {
    expect(stepsForTicket('resolved', me, me, false)).toEqual([]);
  });

  it('asks for a reason only when escalating', () => {
    expect(ticketStepNeedsReason('escalate')).toBe(true);
    expect(ticketStepNeedsReason('assign')).toBe(false);
    expect(ticketStepNeedsReason('release')).toBe(false);
    expect(ticketStepNeedsReason('resolve')).toBe(false);
  });

  it('recognises only the four actions', () => {
    expect(isTicketStep('escalate')).toBe(true);
    expect(isTicketStep('delete')).toBe(false);
    expect(isTicketStep(null)).toBe(false);
  });
});

describe('what an agent should notice', () => {
  it('flags nothing on an ordinary request', () => {
    expect(flagsFor(aTicket())).toEqual([]);
  });

  it('flags escalation as the loudest, then a missed first reply and a request open too long', () => {
    const flags = flagsFor(
      aTicket({ escalated: true, firstReplyOverdue: true, resolutionOverdue: true }),
    );

    expect(flags.map((f) => f.label)).toEqual([
      'Escalated',
      'No first reply in time',
      'Open too long',
    ]);
    expect(flags[0].tone).toBe('danger');
    expect(flags[1].tone).toBe('warn');
  });

  it('explains the Worker’s escalation codes and shows an agent’s own words as written', () => {
    expect(escalationReasonText('first_reply_overdue')).toMatch(/first-reply/);
    expect(escalationReasonText('resolution_overdue')).toMatch(/resolution/);
    expect(escalationReasonText('Refund dispute, third message.')).toBe(
      'Refund dispute, third message.',
    );
    expect(escalationReasonText(null)).toBeNull();
  });
});

describe('ageText', () => {
  const now = new Date('2026-11-03T12:00:00Z');

  it.each([
    ['2026-11-03T11:55:00Z', '5 min'],
    ['2026-11-03T09:00:00Z', '3 h'],
    ['2026-11-02T12:00:00Z', '24 h'],
    ['2026-10-31T12:00:00Z', '3 d'],
  ])('shows %s as %s', (from, expected) => {
    expect(ageText(from, now)).toBe(expected);
  });

  it('never shows a negative age for a time in the future', () => {
    expect(ageText('2026-11-03T13:00:00Z', now)).toBe('0 min');
  });
});
