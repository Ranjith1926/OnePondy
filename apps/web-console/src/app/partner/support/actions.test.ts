import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError, partnerApi } from '@/lib/api';

import { createRequest, replyToRequest } from './actions';

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
  return { ApiError, partnerApi: vi.fn() };
});

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

const call = vi.mocked(partnerApi);
const businessId = 'b0000000-0000-4000-8000-000000000001';
const ticketId = 'd0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

const request = (fields: Record<string, string> = {}) =>
  createRequest(
    { status: 'idle' },
    form({
      businessId,
      topic: 'business',
      subject: 'Menu question',
      message: 'How do I hide a dish?',
      ...fields,
    }),
  );

beforeEach(() => {
  call.mockReset();
  call.mockResolvedValue({ id: ticketId });
});

describe('createRequest', () => {
  it('sends the request about the business and opens it', async () => {
    await expect(request({ subject: '  Menu question  ' })).rejects.toThrow(
      `NEXT_REDIRECT /partner/support/${ticketId}?done=created`,
    );

    expect(call).toHaveBeenCalledWith('/api/v1/support/tickets', {
      method: 'POST',
      json: {
        topic: 'business',
        subject: 'Menu question',
        message: 'How do I hide a dish?',
        businessId,
      },
    });
  });

  it('needs a business, a known topic, a subject and a message, and says which is missing', async () => {
    const state = await request({ businessId: '', topic: 'gossip', subject: ' ', message: '' });

    expect(Object.keys(state.fieldErrors ?? {}).sort()).toEqual([
      'businessId',
      'message',
      'subject',
      'topic',
    ]);
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses a business id that is not a GUID, so nothing odd goes into the request', async () => {
    const state = await request({ businessId: '../../admin' });

    expect(state.fieldErrors?.businessId).toBeDefined();
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses a subject or message that is too long', async () => {
    expect((await request({ subject: 'x'.repeat(121) })).fieldErrors?.subject?.[0]).toContain(
      '120',
    );
    expect((await request({ message: 'x'.repeat(2001) })).fieldErrors?.message?.[0]).toContain(
      '2000',
    );
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the server message when requests are not open yet', async () => {
    call.mockRejectedValue(
      new ApiError(503, 'support_unavailable', 'Support requests are not open yet.'),
    );

    const state = await request();

    expect(state.status).toBe('error');
    expect(state.message).toBe('Support requests are not open yet.');
  });
});

describe('replyToRequest', () => {
  const reply = (fields: Record<string, string> = {}) =>
    replyToRequest({ status: 'idle' }, form({ id: ticketId, body: 'Thank you.', ...fields }));

  it('sends the trimmed reply and returns to the request', async () => {
    await expect(reply({ body: '  Thank you.  ' })).rejects.toThrow(
      `NEXT_REDIRECT /partner/support/${ticketId}?done=reply`,
    );

    expect(call).toHaveBeenCalledWith(`/api/v1/support/tickets/${ticketId}/messages`, {
      method: 'POST',
      json: { body: 'Thank you.' },
    });
  });

  it('will not send an empty or over-long reply, or to an id that is not a GUID', async () => {
    expect((await reply({ body: ' ' })).fieldErrors?.body).toBeDefined();
    expect((await reply({ body: 'x'.repeat(2001) })).fieldErrors?.body?.[0]).toContain('2000');
    expect((await reply({ id: '../../x' })).status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the server message when the request was resolved too long ago', async () => {
    call.mockRejectedValue(
      new ApiError(
        409,
        'support_cannot_reopen',
        'This request was resolved a while ago. Please start a new one.',
      ),
    );

    const state = await reply();

    expect(state.message).toContain('start a new one');
  });
});
