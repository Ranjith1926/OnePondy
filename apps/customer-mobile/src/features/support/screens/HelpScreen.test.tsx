import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { aGrievanceOfficer, aTicketSummary, ticketId } from '../../../test-utils/supportFixture';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';
import { supportApi } from '../api/supportApi';
import { HelpScreen } from './HelpScreen';

const mockNavigate = jest.fn();
const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => {
  const { useEffect } = jest.requireActual('react');
  return {
    useNavigation: () => ({ navigate: mockNavigate, goBack: mockGoBack }),
    // A screen is focused as soon as it is shown.
    useFocusEffect: (effect: () => void) => useEffect(effect, [effect]),
  };
});
jest.mock('../../modules/hooks/useAppConfig', () => ({ useModuleStatus: jest.fn() }));
jest.mock('../api/supportApi', () => ({ supportApi: { list: jest.fn(), grievance: jest.fn() } }));

const list = supportApi.list as jest.Mock;
const grievance = supportApi.grievance as jest.Mock;
const moduleStatus = useModuleStatus as jest.Mock;

async function renderScreen(state = signedInState) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <HelpScreen />
    </QueryClientProvider>,
    { initialState: state },
  );
}

beforeEach(() => {
  list.mockReset();
  grievance.mockReset();
  mockNavigate.mockReset();
  mockGoBack.mockReset();
  grievance.mockResolvedValue(aGrievanceOfficer());
  moduleStatus.mockReturnValue({ support: 'available' });
});

describe('<HelpScreen />', () => {
  it('lists the customer’s requests with where each one stands', async () => {
    list.mockResolvedValue([
      aTicketSummary(),
      aTicketSummary({ id: 't2', subject: 'Refund not received', status: 'waitingForRequester' }),
    ]);

    await renderScreen();

    expect(await screen.findByText('Where is my order?')).toBeOnTheScreen();
    expect(screen.getByText('Refund not received')).toBeOnTheScreen();
    expect(screen.getByText(/^Waiting for OnePondy · /)).toBeOnTheScreen();
    expect(screen.getByText(/^OnePondy has replied · /)).toBeOnTheScreen();
  });

  it('opens a request when it is tapped', async () => {
    list.mockResolvedValue([aTicketSummary()]);
    await renderScreen();

    await userEvent.setup().press(await screen.findByText('Where is my order?'));

    expect(mockNavigate).toHaveBeenCalledWith('SupportTicket', { id: ticketId });
  });

  it('starts a new request', async () => {
    list.mockResolvedValue([]);
    await renderScreen();

    expect(await screen.findByText("You haven't asked for help yet.")).toBeOnTheScreen();
    await userEvent.setup().press(screen.getByRole('button', { name: 'New request' }));

    expect(mockNavigate).toHaveBeenCalledWith('NewSupportRequest');
  });

  it('says so, and lets the customer retry, when requests cannot be loaded', async () => {
    list.mockRejectedValueOnce(new Error('offline'));
    await renderScreen();
    expect(await screen.findByText("Couldn't load your requests")).toBeOnTheScreen();

    list.mockResolvedValueOnce([aTicketSummary()]);
    await userEvent.setup().press(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Where is my order?')).toBeOnTheScreen();
  });

  it('asks a guest to sign in, and does not ask the server for requests', async () => {
    await renderScreen(guestState);

    expect(
      await screen.findByText('Sign in to send a request and follow the answers.'),
    ).toBeOnTheScreen();
    expect(list).not.toHaveBeenCalled();
    await userEvent.setup().press(screen.getByRole('button', { name: 'Sign in' }));
    expect(mockNavigate).toHaveBeenCalledWith('SignIn');
  });

  it('says requests are not open while the support module is not available, and offers no new request', async () => {
    moduleStatus.mockReturnValue({ support: 'comingSoon' });

    await renderScreen();

    expect(await screen.findByText(/Support requests are not open yet/)).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'New request' })).not.toBeOnTheScreen();
    expect(list).not.toHaveBeenCalled();
  });

  it('always says who the grievance officer is, even to a guest and while requests are closed', async () => {
    moduleStatus.mockReturnValue({ support: 'comingSoon' });
    grievance.mockResolvedValue(
      aGrievanceOfficer({
        configured: true,
        name: 'R. Kumar',
        email: 'grievance@onepondy.example',
        phone: '+91 413 000 0000',
        address: '1 Rue Suffren, Puducherry',
      }),
    );

    await renderScreen(guestState);

    expect(await screen.findByText('R. Kumar')).toBeOnTheScreen();
    expect(screen.getByText('Email: grievance@onepondy.example')).toBeOnTheScreen();
    expect(screen.getByText('Phone: +91 413 000 0000')).toBeOnTheScreen();
    expect(screen.getByText('Address: 1 Rue Suffren, Puducherry')).toBeOnTheScreen();
    expect(
      screen.getByText(
        'We acknowledge a complaint within 48 hours and aim to resolve it within 30 days.',
      ),
    ).toBeOnTheScreen();
  });

  it('says plainly when no grievance officer has been appointed yet', async () => {
    await renderScreen(guestState);

    expect(
      await screen.findByText('A grievance officer has not been appointed yet.'),
    ).toBeOnTheScreen();
  });
});
