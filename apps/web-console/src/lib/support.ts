import type { AgentTicketSummary, SupportStatus } from './apiTypes';

/** What the agents' list can show. The API decides what each means; the console only names them. */
export type AgentView =
  'needs_reply' | 'waiting' | 'mine' | 'unassigned' | 'escalated' | 'overdue' | 'resolved' | 'all';

export const agentViews: readonly { key: AgentView; label: string }[] = [
  { key: 'needs_reply', label: 'Needs a reply' },
  { key: 'waiting', label: 'Waiting for the person' },
  { key: 'mine', label: 'Mine' },
  { key: 'unassigned', label: 'Unassigned' },
  { key: 'escalated', label: 'Escalated' },
  { key: 'overdue', label: 'Overdue' },
  { key: 'resolved', label: 'Resolved' },
  { key: 'all', label: 'All' },
];

/** The list from the address bar; anything unknown means the queue of requests that need a reply. */
export const parseAgentView = (value: string | undefined): AgentView =>
  agentViews.find((view) => view.key === value)?.key ?? 'needs_reply';

/** An agent's actions on a request. Each is one POST to `/admin/support/tickets/{id}/{step}` carrying the request's version. */
export type TicketStep = 'assign' | 'release' | 'resolve' | 'escalate';

export const ticketSteps: readonly TicketStep[] = ['assign', 'release', 'resolve', 'escalate'];

export const isTicketStep = (value: unknown): value is TicketStep =>
  typeof value === 'string' && (ticketSteps as readonly string[]).includes(value);

/** Escalating needs a reason: it is what an administrator reads first. */
export const ticketStepNeedsReason = (step: TicketStep): boolean => step === 'escalate';

export interface TicketStepOption {
  step: TicketStep;
  label: string;
  pendingLabel: string;
  primary: boolean;
}

/**
 * What an agent can do with a request. This only decides what to offer: the API checks every move, so an offer that is out of date is
 * refused rather than applied. A resolved request offers nothing; the person reopens it by replying.
 */
export function stepsForTicket(
  status: SupportStatus,
  assignedToUserId: string | null,
  me: string,
  escalated: boolean,
): TicketStepOption[] {
  if (status === 'resolved') {
    return [];
  }

  const steps: TicketStepOption[] = [];
  steps.push(
    assignedToUserId === me
      ? { step: 'release', label: 'Release', pendingLabel: 'Releasing…', primary: false }
      : { step: 'assign', label: 'Take it', pendingLabel: 'Taking…', primary: false },
  );
  steps.push({ step: 'resolve', label: 'Resolve', pendingLabel: 'Resolving…', primary: true });
  if (!escalated) {
    steps.push({
      step: 'escalate',
      label: 'Escalate',
      pendingLabel: 'Escalating…',
      primary: false,
    });
  }

  return steps;
}

/** How long ago, in the largest sensible unit: "5 min", "3 h", "2 d". */
export function ageText(from: string, now: Date): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - new Date(from).getTime()) / 60_000));
  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);
  return hours < 48 ? `${hours} h` : `${Math.floor(hours / 24)} d`;
}

export interface TicketFlag {
  label: string;
  /** Matches the console's badge tones: escalation and a missed target are the loudest. */
  tone: 'danger' | 'warn';
}

/** What an agent should notice about a request before reading it. */
export function flagsFor(ticket: AgentTicketSummary): TicketFlag[] {
  const flags: TicketFlag[] = [];
  if (ticket.escalated) {
    flags.push({ label: 'Escalated', tone: 'danger' });
  }

  if (ticket.firstReplyOverdue) {
    flags.push({ label: 'No first reply in time', tone: 'warn' });
  }

  if (ticket.resolutionOverdue) {
    flags.push({ label: 'Open too long', tone: 'warn' });
  }

  return flags;
}

/** What the Worker's escalation codes mean, for an administrator reading an escalated request. An agent's own words are shown as written. */
export const escalationReasonLabels: Record<string, string> = {
  first_reply_overdue: 'Nobody replied within the first-reply target.',
  resolution_overdue: 'Still unresolved after the resolution target.',
};

export const escalationReasonText = (reason: string | null): string | null =>
  reason === null ? null : (escalationReasonLabels[reason] ?? reason);

/** The state `revealContact` returns: what was found, or why not. */
export interface ContactState {
  status: 'idle' | 'shown' | 'error';
  phoneNumber?: string | null;
  email?: string | null;
  message?: string;
}
