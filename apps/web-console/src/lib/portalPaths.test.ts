import { describe, expect, it } from 'vitest';

import { isPartnerPortalPath } from './portalPaths';

describe('isPartnerPortalPath', () => {
  it('recognises the partner portal and everything under it', () => {
    expect(isPartnerPortalPath('/partner')).toBe(true);
    expect(isPartnerPortalPath('/partner/sign-in')).toBe(true);
    expect(isPartnerPortalPath('/partner/businesses/abc/orders')).toBe(true);
  });

  it('does not take the staff pages for partner applications for the portal', () => {
    expect(isPartnerPortalPath('/partners')).toBe(false);
    expect(isPartnerPortalPath('/partners/0198-abc')).toBe(false);
    expect(isPartnerPortalPath('/partners/0198-abc/documents/xyz')).toBe(false);
  });

  it('leaves the other staff pages and the root alone', () => {
    expect(isPartnerPortalPath('/')).toBe(false);
    expect(isPartnerPortalPath('/reviews')).toBe(false);
    expect(isPartnerPortalPath('/audit')).toBe(false);
  });
});
