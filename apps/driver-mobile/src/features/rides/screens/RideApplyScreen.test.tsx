import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent, waitFor } from '@testing-library/react-native';

import { aProfile, notApplied } from '../../../test-utils/rideFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { rideDriverApi } from '../api/rideDriverApi';
import { RideApplyScreen } from './RideApplyScreen';

const mockNavigate = jest.fn();
const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate, goBack: mockGoBack }),
}));
jest.mock('../../../session/SessionProvider', () => ({
  useSession: () => ({ status: 'signedIn' }),
}));
jest.mock('../api/rideDriverApi', () => ({
  rideDriverApi: { getMine: jest.fn(), save: jest.fn() },
  vehicleClasses: ['auto', 'cab', 'bikeTaxi'],
}));

const api = rideDriverApi as unknown as Record<string, jest.Mock>;

async function renderScreen() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <RideApplyScreen />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  api.getMine.mockReset();
  api.save.mockReset();
  mockNavigate.mockReset();
});

describe('<RideApplyScreen />', () => {
  it('needs every field before it sends anything', async () => {
    api.getMine.mockResolvedValue(notApplied);
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByTestId('ride-apply-submit'));

    expect(screen.getByText('Enter your full name.')).toBeOnTheScreen();
    expect(screen.getByText('Choose your vehicle.')).toBeOnTheScreen();
    expect(screen.getByText('Enter your permit number.')).toBeOnTheScreen();
    expect(screen.getByText(/Enter a date that has not passed/)).toBeOnTheScreen();
    expect(api.save).not.toHaveBeenCalled();
  });

  it('refuses an insurance date that has passed', async () => {
    api.getMine.mockResolvedValue(notApplied);
    const user = userEvent.setup();
    await renderScreen();
    await user.type(await screen.findByLabelText('Insurance valid until'), '2020-01-01');

    await user.press(screen.getByTestId('ride-apply-submit'));

    expect(screen.getByText(/Enter a date that has not passed/)).toBeOnTheScreen();
    expect(api.save).not.toHaveBeenCalled();
  });

  it('sends the application with every number tidied, then opens ride mode', async () => {
    api.getMine.mockResolvedValue(notApplied);
    api.save.mockResolvedValue(aProfile({ approvalStatus: 'pending' }));
    const user = userEvent.setup();
    await renderScreen();
    await user.type(await screen.findByLabelText('Full name'), '  Ravi Kumar ');
    await user.press(screen.getByRole('button', { name: 'Auto' }));
    await user.type(screen.getByLabelText('Registration number'), 'py 01 ab 1234');
    await user.type(screen.getByLabelText('Driving licence number'), 'PY-0120200012345');
    await user.type(screen.getByLabelText('Permit number'), 'permit-0001');
    await user.type(screen.getByLabelText('Insurance policy number'), 'policy 778899');
    await user.type(screen.getByLabelText('Insurance valid until'), '2099-03-31');
    await user.press(screen.getByTestId('ride-apply-submit'));

    await waitFor(() =>
      expect(api.save).toHaveBeenCalledWith({
        fullName: 'Ravi Kumar',
        vehicleClass: 'auto',
        vehicleNumber: 'PY01AB1234',
        licenceNumber: 'PY0120200012345',
        permitNumber: 'PERMIT0001',
        insurancePolicyNumber: 'POLICY778899',
        insuranceValidUntil: '2099-03-31',
        version: undefined,
      }),
    );
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('RideHome'));
  });

  it('warns an approved driver that changing details sends them back for review, and sends the version', async () => {
    api.getMine.mockResolvedValue(aProfile());
    api.save.mockResolvedValue(aProfile({ approvalStatus: 'pending', eligible: false }));
    const user = userEvent.setup();
    await renderScreen();

    expect(await screen.findByText(/sends them for review again/)).toBeOnTheScreen();
    await user.press(screen.getByTestId('ride-apply-submit'));

    await waitFor(() =>
      expect(api.save).toHaveBeenCalledWith(expect.objectContaining({ version: 3 })),
    );
  });
});
