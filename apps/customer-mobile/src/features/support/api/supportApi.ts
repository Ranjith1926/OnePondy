import { apiRequest } from '../../../api/apiClient';

// Shapes returned by /api/v1/support (spec Phase 10, step 10.3). Enums arrive in camelCase.

export type SupportTopic = 'booking' | 'foodOrder' | 'payment' | 'account' | 'business' | 'other';

export const supportTopics: readonly SupportTopic[] = [
  'booking',
  'foodOrder',
  'payment',
  'account',
  'business',
  'other',
];

/** Open: waiting for OnePondy. Waiting for requester: support has answered. Resolved: closed, though replying can reopen it for a while. */
export type TicketStatus = 'open' | 'waitingForRequester' | 'resolved';

/** What a request is about, in a few lines. */
export interface LinkedSubject {
  kind: 'booking' | 'food_order';
  id: string;
  title: string;
  status: string;
  totalAmount: number;
  currency: string;
  when: string;
}

/** A message in a request. Support's messages carry no staff member's name. */
export interface TicketMessage {
  id: string;
  fromSupport: boolean;
  body: string;
  createdAt: string;
}

export interface TicketSummary {
  id: string;
  /** A short code to quote to support. */
  reference: string;
  topic: SupportTopic;
  subject: string;
  status: TicketStatus;
  createdAt: string;
  updatedAt: string;
}

export interface TicketDetail {
  id: string;
  reference: string;
  topic: SupportTopic;
  subject: string;
  status: TicketStatus;
  about: LinkedSubject | null;
  messages: TicketMessage[];
  /** False once the request was resolved too long ago to reopen. */
  canReply: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface GrievanceOfficer {
  configured: boolean;
  name: string | null;
  email: string | null;
  phone: string | null;
  address: string | null;
  acknowledgeWithinHours: number;
  resolveWithinDays: number;
}

export interface NewTicketInput {
  topic: SupportTopic;
  subject: string;
  message: string;
  bookingId?: string;
  foodOrderId?: string;
}

export const supportApi = {
  grievance: () => apiRequest<GrievanceOfficer>('/api/v1/support/grievance'),

  list: () => apiRequest<TicketSummary[]>('/api/v1/support/tickets', { auth: true }),

  get: (id: string) =>
    apiRequest<TicketDetail>(`/api/v1/support/tickets/${encodeURIComponent(id)}`, { auth: true }),

  create: (input: NewTicketInput) =>
    apiRequest<TicketDetail>('/api/v1/support/tickets', {
      method: 'POST',
      body: input,
      auth: true,
    }),

  reply: (id: string, body: string) =>
    apiRequest<TicketDetail>(`/api/v1/support/tickets/${encodeURIComponent(id)}/messages`, {
      method: 'POST',
      body: { body },
      auth: true,
    }),
};
