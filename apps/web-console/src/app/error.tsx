'use client';

import { Button } from '@/components/Button';

/** Shown when a page cannot load, most often because the API is unreachable. */
export default function ErrorPage({ reset }: { error: Error; reset: () => void }) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-3 px-4 text-center">
      <h1 className="text-2xl font-bold">This page could not load</h1>
      <p className="max-w-md text-muted">
        The OnePondy API may be unavailable. Try again in a moment; if it keeps happening, tell the
        technical team.
      </p>
      <Button onClick={reset}>Try again</Button>
    </main>
  );
}
