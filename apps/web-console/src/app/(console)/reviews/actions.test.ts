import { beforeEach, describe, expect, it, vi } from 'vitest';

import { api, ApiError } from '@/lib/api';
import { requireEditor } from '@/lib/session';

import { moderateReview } from './actions';

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

vi.mock('@/lib/session', () => ({ requireEditor: vi.fn() }));

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
const reviewId = 'c0000000-0000-4000-8000-000000000001';

function form(fields: Record<string, string>) {
  const data = new FormData();
  data.set('id', reviewId);
  data.set('version', '4242');
  for (const [name, value] of Object.entries(fields)) {
    data.set(name, value);
  }
  return data;
}

const run = (fields: Record<string, string>) => moderateReview({ status: 'idle' }, form(fields));

/** Runs the action once and returns where it redirected to (a successful action always redirects). */
async function redirectedTo(fields: Record<string, string>): Promise<string> {
  try {
    await run(fields);
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
  vi.mocked(requireEditor).mockReset();
});

describe('moderateReview', () => {
  it('checks the person is a moderator before anything else', async () => {
    await redirectedTo({ step: 'publish' });

    expect(requireEditor).toHaveBeenCalledTimes(1);
  });

  it('publishes with just the version it was shown, then returns to the queue it came from', async () => {
    const to = await redirectedTo({ step: 'publish', view: 'pending' });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/reviews/${reviewId}/publish`, {
      method: 'POST',
      json: { version: 4242 },
    });
    expect(to).toBe('/reviews?status=pending&done=publish');
  });

  it('rejects with a trimmed reason, and keeps the filter the moderator was on', async () => {
    const to = await redirectedTo({
      step: 'reject',
      reason: '  Contains a phone number.  ',
      view: 'published',
    });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/reviews/${reviewId}/reject`, {
      method: 'POST',
      json: { version: 4242, reason: 'Contains a phone number.' },
    });
    expect(to).toBe('/reviews?status=published&done=reject');
  });

  it('takes a review down with a reason', async () => {
    await redirectedTo({ step: 'remove', reason: 'Reported as abusive.' });

    expect(call).toHaveBeenCalledWith(`/api/v1/admin/reviews/${reviewId}/remove`, {
      method: 'POST',
      json: { version: 4242, reason: 'Reported as abusive.' },
    });
  });

  it.each(['reject', 'remove'])(
    'will not %s without a reason, and does not ask the server',
    async (step) => {
      const state = await run({ step, reason: '   ' });

      expect(state.fieldErrors?.reason).toBeDefined();
      expect(call).not.toHaveBeenCalled();
    },
  );

  it('refuses a reason that is too long', async () => {
    const state = await run({ step: 'reject', reason: 'x'.repeat(501) });

    expect(state.fieldErrors?.reason?.[0]).toContain('500');
    expect(call).not.toHaveBeenCalled();
  });

  it.each(['', 'abc', '-3', '1.5'])(
    'refuses the version "%s" instead of turning it into 0',
    async (version) => {
      const state = await run({ step: 'publish', version });

      expect(state.status).toBe('error');
      expect(call).not.toHaveBeenCalled();
    },
  );

  it('refuses an action it does not know', async () => {
    const state = await run({ step: 'delete' });

    expect(state.status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('refuses an id that is not a GUID, so nothing odd goes into a request path', async () => {
    const state = await run({ step: 'publish', id: '../../admin/staff' });

    expect(state.status).toBe('error');
    expect(call).not.toHaveBeenCalled();
  });

  it('shows the server message when the review has moved on', async () => {
    call.mockRejectedValue(
      new ApiError(
        409,
        'review_wrong_state',
        'This review is not in a state where that can be done.',
      ),
    );

    const state = await run({ step: 'publish' });

    expect(state.status).toBe('error');
    expect(state.message).toContain('not in a state');
  });

  it('falls back to the waiting queue when the filter is not one it knows', async () => {
    const to = await redirectedTo({ step: 'publish', view: 'everything' });

    expect(to).toBe('/reviews?status=pending&done=publish');
  });
});
