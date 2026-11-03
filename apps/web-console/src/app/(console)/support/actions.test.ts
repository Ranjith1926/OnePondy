import { beforeEach, describe, expect, it, vi } from 'vitest';

import { api, ApiError } from '@/lib/api';
import { requireSupport } from '@/lib/session';

import { changeTicket, replyToTicket, revealContact } from './actions';

vi.mock('@/lib/api', () => {
  class ApiError extends Error {
    constructor(
      readonly status: number,
      readonly code: string,
      message: string,
      readonly fieldErrors: Record<string, string[]> = {},
    ) {
      super(message);
    }
  }
  return { ApiError, api: vi.fn() };
});

vi.mock('@/lib/session', () => ({ requireSupport: vi.fn() }));

// A Next.js redirect is an exception the framework catches; here it is one the test can read.
vi.mock('next/navigation', () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT ${url}`);
  },
  unstable_rethrow: (error: unknown) => {
    if (error instanceof Error && error.message.startsWith('NEXT_REDIRECT')) {
      throw error;
    }
  },
}));

const call = vi.mocked(api);
const ticketId = 'd0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('id', ticketId);
  data.set('version', '4242');
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

async function redirectedTo(run: () => Promise<unknown>): Promise<string> {
  try {
    await run();
  } catch (error) {
    const message = (error as Error).message;
    if (message.startsWith('NEXT_REDIRECT ')) {
      return message.slice('NEXT_REDIRECT '.length);
    }
    throw error;
  }
  throw new Error('expected the action to redirect');
}

beforeEach(() => {
  call.mockReset();
  call.mockResolvedValue(undefined);
  vi.mocked(requireSupport).mockReset();
});

describe('replyToTicket', () => {
  const reply = (fields: Record<string, string> = {}) =>
    replyToTicket({ status: 'idle' }, form({ body: 'Happy to help.', ...fields }));

  it('checks the person works support before anything else', async () => {
    await redirectedTo(() => reply());

    expect(requireSupport).toHaveBeenCalledTimes(1);
  });

  it('sends the trimmed reply with the version it was shown, then stays on the request', async () => {
    const to = await redirectedTo(() => reply({ body: '  Looking into it.  ' }));

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/support/tickets/${ticketId}/messages`, {
      method: 'POST',
      json: { version: 4242, body: 'Looking into it.' },
    });
    expect(to).toBe(`/support/${ticketId}?done=reply`);
  });

  it('will not send an empty or over-long reply', async () => {
    expect((await reply({ body: '   ' })).fieldErrors?.body).toBeDefined();
    expect((await reply({ body: 'x'.repeat(2001) })).fieldErrors?.body?.[0]).toContain('2000');
    expect(call).not.toHaveBeenCalled();
  });

  it.each(['', 'abc', '-1'])(
    'refuses the version "%s" instead of turning it into 0',
    async (version) => {
      expect((await reply({ version })).status).toBe('error');
      expect(call).not.toHaveBeenCalled();
    },
  );

  it('refuses an id that is not a GUID, so nothing odd goes into a request path', async () => {
    expect((await reply({ id: '../../admin/staff' })).status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the server message when the request has been resolved', async () => {
    call.mockRejectedValue(new ApiError(409, 'support_resolved', 'This request is resolved.'));

    const state = await reply();

    expect(state.status).toBe('error');
    expect(state.message).toBe('This request is resolved.');
  });
});

describe('changeTicket', () => {
  const change = (fields: Record<string, string>) => changeTicket({ status: 'idle' }, form(fields));

  it.each(['assign', 'release'])(
    '%s sends just the version and stays on the request',
    async (step) => {
      const to = await redirectedTo(() => change({ step }));

      expect(call).toHaveBeenCalledWith(`/api/v1/admin/support/tickets/${ticketId}/${step}`, {
        method: 'POST',
        json: { version: 4242 },
      });
      expect(to).toBe(`/support/${ticketId}?done=${step}`);
    },
  );

  it('resolving sends the version and returns to the queue', async () => {
    const to = await redirectedTo(() => change({ step: 'resolve' }));

    expect(to).toBe('/support?done=resolve');
    expect(call).toHaveBeenCalledWith(expect.any(String), {
      method: 'POST',
      json: { version: 4242 },
    });
  });

  it('escalating sends the trimmed reason', async () => {
    const to = await redirectedTo(() =>
      change({ step: 'escalate', reason: '  Third message, no answer.  ' }),
    );

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/support/tickets/${ticketId}/escalate`, {
      method: 'POST',
      json: { version: 4242, reason: 'Third message, no answer.' },
    });
    expect(to).toBe(`/support/${ticketId}?done=escalate`);
  });

  it('will not escalate without a reason, or with a reason that is too long', async () => {
    expect((await change({ step: 'escalate', reason: '  ' })).fieldErrors?.reason).toBeDefined();
    expect(
      (await change({ step: 'escalate', reason: 'x'.repeat(201) })).fieldErrors?.reason?.[0],
    ).toContain('200');
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses an action it does not know, a bad id and a bad version', async () => {
    expect((await change({ step: 'delete' })).status).toBe('error');
    expect((await change({ step: 'assign', id: 'nope' })).status).toBe('error');
    expect((await change({ step: 'assign', version: '' })).status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the server message when the request moved on meanwhile', async () => {
    call.mockRejectedValue(
      new ApiError(409, 'support_changed', 'This request changed while you were looking at it.'),
    );

    const state = await change({ step: 'resolve' });

    expect(state.message).toContain('changed while you were looking');
  });
});

describe('revealContact', () => {
  const reveal = (fields: Record<string, string> = {}) =>
    revealContact({ status: 'idle' }, form(fields));

  it('asks for the contact details of this request only, and shows them', async () => {
    call.mockResolvedValue({ phoneNumber: '+919988776655', email: null });

    const state = await reveal();

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/support/tickets/${ticketId}/contact`);
    expect(state).toEqual({ status: 'shown', phoneNumber: '+919988776655', email: null });
    expect(requireSupport).toHaveBeenCalledTimes(1);
  });

  it('refuses an id that is not a GUID without asking the server', async () => {
    const state = await reveal({ id: '../../admin/staff' });

    expect(state.status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('reports a failure instead of showing nothing', async () => {
    call.mockRejectedValue(new ApiError(404, 'ticket_not_found', 'That request does not exist.'));

    const state = await reveal();

    expect(state.status).toBe('error');
    expect(state.message).toBe('That request does not exist.');
  });
});
