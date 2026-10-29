import Link from 'next/link';

const tabs = [
  { href: '/payments', label: 'Payments' },
  { href: '/payments/refunds', label: 'Refunds' },
  { href: '/payments/exceptions', label: 'Reconciliation' },
];

/** The money screens share one row of links, so an administrator can move between them. */
export function PaymentsTabs({ current }: { current: string }) {
  return (
    <nav aria-label="Payments sections" className="mb-6 flex flex-wrap gap-2">
      {tabs.map((tab) => (
        <Link
          key={tab.href}
          href={tab.href}
          aria-current={tab.href === current ? 'page' : undefined}
          className="rounded-lg border border-line-strong px-4 py-2 text-sm font-semibold hover:bg-canvas aria-[current=page]:border-brand aria-[current=page]:bg-brand aria-[current=page]:text-white"
        >
          {tab.label}
        </Link>
      ))}
    </nav>
  );
}
