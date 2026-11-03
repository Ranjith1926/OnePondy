import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';
import { ProfileScreen } from './ProfileScreen';

const mockNavigate = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate }),
}));
jest.mock('../../auth/hooks/useProfile', () => ({
  useProfile: () => ({ data: undefined }),
  formatPhone: (value: string) => value,
}));
jest.mock('../../auth/hooks/useAuthActions', () => ({
  useAuthActions: () => ({ signOut: jest.fn() }),
}));
jest.mock('../../modules/hooks/useAppConfig', () => ({ useModuleStatus: jest.fn() }));

const moduleStatus = useModuleStatus as jest.Mock;

async function renderScreen(state = signedInState) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <ProfileScreen />
    </QueryClientProvider>,
    { initialState: state },
  );
}

beforeEach(() => {
  mockNavigate.mockReset();
  moduleStatus.mockReturnValue({ support: 'comingSoon' });
});

describe('<ProfileScreen /> help', () => {
  it('opens Help and support, whether or not requests are open yet, because the grievance officer is always there', async () => {
    await renderScreen();

    await userEvent.setup().press(screen.getByText('Help and support'));

    expect(mockNavigate).toHaveBeenCalledWith('Help');
  });

  it('lets a guest open Help and support too', async () => {
    await renderScreen(guestState);

    await userEvent.setup().press(screen.getByText('Help and support'));

    expect(mockNavigate).toHaveBeenCalledWith('Help');
  });

  it('leaves Help and support out when the support module is hidden', async () => {
    moduleStatus.mockReturnValue({ support: 'hidden' });

    await renderScreen();

    expect(screen.queryByText('Help and support')).not.toBeOnTheScreen();
  });
});
