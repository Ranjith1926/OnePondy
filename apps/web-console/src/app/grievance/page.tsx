import type { Metadata } from 'next';

import { publicApi } from '@/lib/api';
import type { GrievanceOfficer } from '@/lib/apiTypes';

export const metadata: Metadata = {
  title: 'Grievance officer',
  // Unlike the rest of the console, this page is for the public: someone with a complaint must be able to find it.
  robots: { index: true, follow: true },
};

// Read on every visit: who the officer is can change, and a stale name here would send a complaint to the wrong person.
export const dynamic = 'force-dynamic';

/**
 * Who to write to with a complaint about OnePondy. The Consumer Protection (E-Commerce) Rules require a named grievance officer, reachable
 * by anyone, signed in or not. Until one is appointed the page says so, rather than showing a blank or a placeholder.
 */
export default async function GrievancePage() {
  const officer = await publicApi<GrievanceOfficer>('/api/v1/support/grievance');

  return (
    <main className="mx-auto max-w-xl p-6 md:p-10">
      <p className="text-xs font-bold tracking-wide text-brand uppercase">OnePondy</p>
      <h1 className="mt-1 text-2xl font-bold">Grievance officer</h1>
      <p className="mt-2 text-sm text-muted">
        If something has gone wrong and you are not satisfied with the answer you have had, write to
        our grievance officer.
      </p>

      {officer.configured ? (
        <section
          className="mt-6 rounded-xl border border-line bg-surface p-5"
          aria-label="Grievance officer"
        >
          <p className="text-lg font-semibold">{officer.name}</p>
          {officer.email ? (
            <p className="mt-2 text-sm">
              <span className="text-muted">E-mail: </span>
              <a
                className="font-semibold text-brand hover:underline"
                href={`mailto:${officer.email}`}
              >
                {officer.email}
              </a>
            </p>
          ) : null}
          {officer.phone ? (
            <p className="mt-1 text-sm">
              <span className="text-muted">Phone: </span>
              {officer.phone}
            </p>
          ) : null}
          {officer.address ? (
            <p className="mt-1 text-sm whitespace-pre-line">
              <span className="text-muted">Address: </span>
              {officer.address}
            </p>
          ) : null}
          <p className="mt-4 text-sm text-muted">
            We acknowledge a complaint within {officer.acknowledgeWithinHours} hours and aim to
            resolve it within {officer.resolveWithinDays} days.
          </p>
        </section>
      ) : (
        <p className="mt-6 rounded-xl border border-line bg-surface p-5 text-sm">
          A grievance officer has not been appointed yet.
        </p>
      )}
    </main>
  );
}
