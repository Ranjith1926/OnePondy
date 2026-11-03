import type { AlertSeverity } from './apiTypes';

/** Where an alert's `area` leads in the console. Anything unknown stays on the operations page. */
const areaLinks: Record<string, { href: string; label: string }> = {
  payments: { href: '/payments/exceptions', label: 'Open payment differences' },
  support: { href: '/support?view=overdue', label: 'Open support' },
  orders: { href: '/orders?attention=1', label: 'Open orders that need attention' },
  reviews: { href: '/reviews', label: 'Open the review queue' },
  safety: { href: '/safety', label: 'Open the safety queue' },
};

export const alertLink = (area: string): { href: string; label: string } | null =>
  areaLinks[area] ?? null;

export const severityLabels: Record<AlertSeverity, string> = {
  critical: 'Act now',
  warning: 'Look today',
};

/** What each class of request is, for the limiter's table. The names are the API's. */
export const rateLimitClassLabels: Record<string, string> = {
  auth: 'Sign-in and account',
  browse: 'Public reading',
  read: 'Reading while signed in',
  write: 'Changes',
  create: 'Starting something new',
  upload: 'Photos and documents',
  staff: 'Console',
};

export const rateLimitClassLabel = (name: string): string => rateLimitClassLabels[name] ?? name;

/** "3 d", "5 h", "12 min": how long something has waited, or null when nothing is waiting. */
export function waitedText(since: string | null, now: Date): string | null {
  if (since === null) {
    return null;
  }

  const minutes = Math.max(0, Math.floor((now.getTime() - new Date(since).getTime()) / 60_000));
  if (minutes < 60) {
    return `${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);
  return hours < 48 ? `${hours} h` : `${Math.floor(hours / 24)} d`;
}
