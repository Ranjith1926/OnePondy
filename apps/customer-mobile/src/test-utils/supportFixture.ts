import type {
  GrievanceOfficer,
  TicketDetail,
  TicketMessage,
  TicketSummary,
} from '../features/support/api/supportApi';

export const ticketId = 'd0000000-0000-4000-8000-000000000001';

export function aTicketSummary(overrides: Partial<TicketSummary> = {}): TicketSummary {
  return {
    id: ticketId,
    reference: 'D0000001',
    topic: 'foodOrder',
    subject: 'Where is my order?',
    status: 'open',
    createdAt: '2026-11-03T08:00:00Z',
    updatedAt: '2026-11-03T08:00:00Z',
    ...overrides,
  };
}

export function aTicketMessage(overrides: Partial<TicketMessage> = {}): TicketMessage {
  return {
    id: 'm0000000-0000-4000-8000-000000000001',
    fromSupport: false,
    body: 'I ordered a while ago and nothing has arrived.',
    createdAt: '2026-11-03T08:00:00Z',
    ...overrides,
  };
}

export function aTicket(overrides: Partial<TicketDetail> = {}): TicketDetail {
  return {
    ...aTicketSummary(),
    about: null,
    messages: [aTicketMessage()],
    canReply: true,
    ...overrides,
  };
}

export function aGrievanceOfficer(overrides: Partial<GrievanceOfficer> = {}): GrievanceOfficer {
  return {
    configured: false,
    name: null,
    email: null,
    phone: null,
    address: null,
    acknowledgeWithinHours: 48,
    resolveWithinDays: 30,
    ...overrides,
  };
}
