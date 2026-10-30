import type { ReactNode } from 'react';

import { Button } from '@/components/Button';
import { isAdministrator, roleLabels } from '@/lib/roles';
import { requireUser } from '@/lib/session';

import { signOut } from './actions';
import { Navigation, type NavItem } from './Navigation';

export default async function ConsoleLayout({ children }: { children: ReactNode }) {
  const user = await requireUser();

  // Links follow the role; the API enforces the same rules for every request.
  const items: NavItem[] = [
    { href: '/', label: 'Dashboard' },
    { href: '/places', label: 'Places' },
    { href: '/configuration', label: 'Configuration' },
    ...(isAdministrator(user.role)
      ? [
          { href: '/partners', label: 'Partners' },
          { href: '/drivers', label: 'Drivers' },
          { href: '/listings', label: 'Listings' },
          { href: '/payments', label: 'Payments' },
          { href: '/settlements', label: 'Statements' },
          { href: '/staff', label: 'Staff' },
          { href: '/audit', label: 'Audit log' },
        ]
      : []),
  ];

  return (
    <div className="min-h-screen md:flex">
      <aside className="border-b border-line bg-surface p-4 md:sticky md:top-0 md:h-screen md:w-60 md:shrink-0 md:border-r md:border-b-0">
        <div className="mb-4 px-3">
          <p className="text-xs font-bold tracking-wide text-brand uppercase">OnePondy</p>
          <p className="text-lg font-bold">Console</p>
        </div>
        <Navigation items={items} />
        <div className="mt-6 border-t border-line px-3 pt-4">
          <p className="text-sm font-semibold">{user.name}</p>
          <p className="text-xs text-muted">{roleLabels[user.role]}</p>
          <form action={signOut} className="mt-3">
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
