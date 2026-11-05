/**
 * The partner portal lives under /partner. The staff console has its own pages under /partners (partner applications), which share the
 * first seven letters and must not be mistaken for it: they use the staff session, not the partner one.
 */
export function isPartnerPortalPath(pathname: string): boolean {
  return pathname === '/partner' || pathname.startsWith('/partner/');
}
