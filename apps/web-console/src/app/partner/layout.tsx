import type { ReactNode } from 'react';

import { Button } from '@/components/Button';
import { Navigation, type NavItem } from '@/app/(console)/Navigation';
import { getPartnerUser } from '@/lib/session';

import { signOutPartner } from './actions';

const items: NavItem[] = [{ href: '/partner', label: 'My businesses' }];

export default async function PartnerLayout({ children }: { children: ReactNode }) {
  // The sign-in page lives inside this layout, so a signed-out visitor must be allowed to see it: sending them to sign-in from here
  // would send them to this very layout again, for ever. Everything else is still protected without the layout's help: the proxy
  // turns a signed-out visitor away from every other partner page, and each page's own API calls redirect to sign-in with no session.
  const user = await getPartnerUser();
  if (!user) {
    return <>{children}</>;
  }

  return (
    <div className="min-h-screen md:flex">
      <aside className="border-b border-line bg-surface p-4 md:sticky md:top-0 md:h-screen md:w-60 md:shrink-0 md:border-r md:border-b-0">
        <div className="mb-4 px-3">
          <p className="text-xs font-bold tracking-wide text-brand uppercase">OnePondy</p>
          <p className="text-lg font-bold">Partner</p>
        </div>
        <Navigation items={items} />
        <div className="mt-6 border-t border-line px-3 pt-4">
          <p className="text-sm font-semibold">{user.name || user.email || user.phoneNumber}</p>
          <form action={signOutPartner} className="mt-3">
            <Button type="submit" variant="secondary" className="w-full">
              Sign out
            </Button>
          </form>
        </div>
      </aside>
      <main className="min-w-0 flex-1 p-4 md:p-8">{children}</main>
    </div>
  );
}
