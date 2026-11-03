import { beforeEach, describe, expect, it, vi } from 'vitest';

import { ApiError, partnerApi } from '@/lib/api';

import { replyToReview } from './actions';

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
const reviewId = 'c0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('businessId', businessId);
  data.set('id', reviewId);
  data.set('version', '4242');
  data.set('text', 'Thank you for coming!');
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

const run = (fields: Record<string, string> = {}) =>
  replyToReview({ status: 'idle' }, form(fields));

beforeEach(() => {
  call.mockReset();
  call.mockResolvedValue(undefined);
});

describe('replyToReview', () => {
  it('sends the trimmed reply with the version it was shown, then returns to the list', async () => {
    await expect(run({ text: '  Thank you for coming!  ' })).rejects.toThrow(
      `NEXT_REDIRECT /partner/businesses/${businessId}/reviews?done=reply`,
    );

    expect(call).toHaveBeenCalledWith(
      `/api/v1/partner/businesses/${businessId}/reviews/${reviewId}/reply`,
      {
        method: 'POST',
        json: { version: 4242, text: 'Thank you for coming!' },
      },
    );
  });

  it('will not send an empty reply', async () => {
    const state = await run({ text: '   ' });

    expect(state.fieldErrors?.text).toBeDefined();
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses a reply that is too long', async () => {
    const state = await run({ text: 'x'.repeat(1001) });

    expect(state.fieldErrors?.text?.[0]).toContain('1000');
    expect(call).not.toHaveBeenCalled();
  });

  it.each(['', 'abc', '-1'])(
    'refuses the version "%s" instead of turning it into 0',
    async (version) => {
      const state = await run({ version });

      expect(state.status).toBe('error');
      expect(call).not.toHaveBeenCalled();
    },
  );

  it.each<Record<string, string>>([{ businessId: '../../admin' }, { id: 'not-a-guid' }])(
    'refuses an id that is not a GUID (%o)',
    async (fields) => {
      const state = await run(fields);

      expect(state.status).toBe('error');
      expect(call).not.toHaveBeenCalled();
    },
  );

  it('shows the server message when the review cannot be answered', async () => {
    call.mockRejectedValue(
      new ApiError(
        409,
        'review_cannot_reply',
        'Only a published review that has no reply yet can be answered.',
      ),
    );

    const state = await run();

    expect(state.status).toBe('error');
    expect(state.message).toContain('no reply yet');
  });
});
