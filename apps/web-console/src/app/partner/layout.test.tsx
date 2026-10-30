import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { getPartnerUser } from '@/lib/session';

import PartnerLayout from './layout';

vi.mock('@/lib/session', () => ({ getPartnerUser: vi.fn() }));
vi.mock('@/app/(console)/Navigation', () => ({ Navigation: () => <nav>menu</nav> }));
vi.mock('./actions', () => ({ signOutPartner: vi.fn() }));

const user = vi.mocked(getPartnerUser);

beforeEach(() => user.mockReset());

// The partner sign-in page is rendered inside this layout. If the layout sent a signed-out visitor to sign-in, they would be sent
// to this layout again and never reach the page (it did, until this was tested).
describe('<PartnerLayout />', () => {
  it('shows the page alone, with no menu, to someone who is not signed in', async () => {
    user.mockResolvedValue(null);

    render(await PartnerLayout({ children: <p>Partner sign-in</p> }));

    expect(screen.getByText('Partner sign-in')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
    expect(screen.queryByText('menu')).not.toBeInTheDocument();
  });

  it('wraps a signed-in partner’s pages in the menu, with their name and a way to sign out', async () => {
    user.mockResolvedValue({ id: 'u1', name: 'Asha', email: null, phoneNumber: '+919800000001' });

    render(await PartnerLayout({ children: <p>My businesses</p> }));

    expect(screen.getByText('My businesses')).toBeInTheDocument();
    expect(screen.getByText('Asha')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    expect(screen.getByText('menu')).toBeInTheDocument();
  });
});
