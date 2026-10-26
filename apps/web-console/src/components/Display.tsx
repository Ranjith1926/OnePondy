import Link from 'next/link';
import type { ReactNode } from 'react';

import type { VerificationStatus } from '@/lib/apiTypes';
import { statusLabels } from '@/lib/labels';

type AlertKind = 'info' | 'success' | 'warning' | 'error';

const alertStyles: Record<AlertKind, string> = {
  info: 'bg-info-soft text-info',
  success: 'bg-ok-soft text-ok',
  warning: 'bg-warn-soft text-warn',
  error: 'bg-danger-soft text-danger',
};

export function Alert({ kind, children }: { kind: AlertKind; children: ReactNode }) {
  return (
    <div
      role={kind === 'error' ? 'alert' : 'status'}
      className={`rounded-lg px-4 py-3 text-sm font-medium ${alertStyles[kind]}`}
    >
      {children}
    </div>
  );
}

const statusStyles: Record<VerificationStatus, string> = {
  draft: 'border border-line-strong bg-canvas text-muted',
  pending: 'bg-info-soft text-info',
  verified: 'bg-ok-soft text-ok',
  rejected: 'bg-danger-soft text-danger',
};

const pill = 'inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap';

export function StatusBadge({ status }: { status: VerificationStatus }) {
  return <span className={`${pill} ${statusStyles[status]}`}>{statusLabels[status]}</span>;
}

export function Badge({ tone, children }: { tone: 'ok' | 'warn' | 'muted'; children: string }) {
  const tones = {
    ok: 'bg-ok-soft text-ok',
    warn: 'bg-warn-soft text-warn',
    muted: 'border border-line bg-canvas text-muted',
  };
  return <span className={`${pill} ${tones[tone]}`}>{children}</span>;
}

export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="text-2xl font-bold">{title}</h1>
        {description ? <p className="mt-1 max-w-2xl text-sm text-muted">{description}</p> : null}
      </div>
      {actions ? <div className="flex gap-2">{actions}</div> : null}
    </div>
  );
}

export function Card({
  title,
  children,
  className = '',
}: {
  title?: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={`rounded-xl border border-line bg-surface p-5 ${className}`}
      aria-label={title}
    >
      {title ? <h2 className="mb-4 text-base font-bold">{title}</h2> : null}
      {children}
    </section>
  );
}

export const linkButton =
  'inline-flex min-h-10 items-center justify-center rounded-lg bg-brand px-4 text-sm font-semibold text-white hover:bg-brand-dark';

interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  /** Builds the link for a page, keeping the current filters. */
  href: (page: number) => string;
}

export function Pagination({ page, pageSize, totalCount, href }: PaginationProps) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (pages <= 1) {
    return null;
  }
  const link =
    'rounded-lg border border-line-strong px-3 py-1.5 text-sm font-semibold hover:bg-canvas';
  return (
    <nav aria-label="Pages" className="mt-4 flex items-center justify-between text-sm">
      <span className="text-muted">
        Page {page} of {pages} · {totalCount} total
      </span>
      <div className="flex gap-2">
        {page > 1 ? (
          <Link className={link} href={href(page - 1)}>
            Previous
          </Link>
        ) : null}
        {page < pages ? (
          <Link className={link} href={href(page + 1)}>
            Next
          </Link>
        ) : null}
      </div>
    </nav>
  );
}
