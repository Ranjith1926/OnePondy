'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';

export interface NavItem {
  href: string;
  label: string;
}

export function Navigation({ items }: { items: NavItem[] }) {
  const pathname = usePathname();
  const isCurrent = (href: string) => (href === '/' ? pathname === '/' : pathname.startsWith(href));

  return (
    <nav aria-label="Console">
      <ul className="flex flex-row gap-1 overflow-x-auto md:flex-col">
        {items.map((item) => (
          <li key={item.href}>
            <Link
              href={item.href}
              aria-current={isCurrent(item.href) ? 'page' : undefined}
              className="block rounded-lg px-3 py-2 text-sm font-semibold whitespace-nowrap text-ink hover:bg-brand-soft aria-[current=page]:bg-brand aria-[current=page]:text-white"
            >
              {item.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
