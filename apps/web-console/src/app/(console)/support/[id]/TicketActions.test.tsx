import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { SupportStatus } from '@/lib/apiTypes';

import { ContactReveal } from './ContactReveal';
import { ReplyForm } from './ReplyForm';
import { TicketActions } from './TicketActions';

// The server actions reach the API and the session; these tests are about what the buttons offer.
vi.mock('../actions', () => ({
  replyToTicket: vi.fn(),
  changeTicket: vi.fn(),
  revealContact: vi.fn(),
}));

const id = 'd0000000-0000-4000-8000-000000000001';

const show = (status: SupportStatus, assignedToUserId: string | null = null, escalated = false) =>
  render(
    <TicketActions
      id={id}
      version={4242}
      status={status}
      assignedToUserId={assignedToUserId}
      me="u-me"
      escalated={escalated}
    />,
  );

const hidden = (container: HTMLElement, name: string) =>
  container.querySelector<HTMLInputElement>(`input[type="hidden"][name="${name}"]`)?.value;

describe('<TicketActions />', () => {
  it('offers Take it, Resolve and Escalate for an unassigned request', () => {
    show('open');

    expect(screen.getByRole('button', { name: 'Take it' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Resolve' })).toBeInTheDocument();
    // "Escalate" is both the heading that opens the reason form and its submit button.
    expect(screen.getAllByText('Escalate')).toHaveLength(2);
  });

  it('offers Release instead of Take it on the agent’s own request', () => {
    show('open', 'u-me');

    expect(screen.getByRole('button', { name: 'Release' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Take it' })).not.toBeInTheDocument();
  });

  it('asks why before escalating, and says an administrator reads it', () => {
    show('open');

    expect(screen.getByLabelText('Why is this being escalated?')).toBeInTheDocument();
    expect(screen.getByText('An administrator reads this first.')).toBeInTheDocument();
  });

  it('offers no Escalate on one that is already escalated', () => {
    show('open', null, true);

    expect(screen.queryByText('Escalate')).not.toBeInTheDocument();
  });

  it('offers nothing for a resolved request', () => {
    const { container } = show('resolved');

    expect(container).toBeEmptyDOMElement();
  });

  it('sends the version the agent was looking at', () => {
    const { container } = show('open');

    expect(hidden(container, 'version')).toBe('4242');
    expect(hidden(container, 'id')).toBe(id);
  });
});

describe('<ReplyForm />', () => {
  it('says the person sees the reply without the agent’s name, and sends the version', () => {
    const { container } = render(<ReplyForm id={id} version={4242} />);

    expect(screen.getByText(/without your name/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send reply' })).toBeInTheDocument();
    expect(hidden(container, 'version')).toBe('4242');
  });
});

describe('<ContactReveal />', () => {
  it('does not show the contact details until they are asked for, and says asking is recorded', () => {
    render(<ContactReveal id={id} />);

    expect(screen.getByRole('button', { name: 'Show contact details' })).toBeInTheDocument();
    expect(screen.getByText(/Asking is recorded/)).toBeInTheDocument();
    expect(screen.queryByTestId('contact')).not.toBeInTheDocument();
    expect(screen.queryByText(/\+91/)).not.toBeInTheDocument();
  });
});
