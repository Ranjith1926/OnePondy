import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import { Alert, Linking, Share } from 'react-native';

import { ApiError } from '../../../api/apiErrors';
import { aQuote, aRide, someQuotes, withDriver } from '../../../test-utils/rideFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';
import { ridesApi } from '../api/ridesApi';
import { RideScreen } from './RideScreen';

const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ goBack: mockGoBack, navigate: jest.fn() }),
}));
jest.mock('../../modules/hooks/useAppConfig', () => ({ useModuleStatus: jest.fn() }));
jest.mock('../hooks/useRideLive', () => ({ useRideLive: () => ({ connected: true }) }));
jest.mock('../../location/hooks/useGpsLocation', () => ({
  useGpsLocation: () => ({
    status: 'idle',
    requestLocation: jest.fn(),
    clear: jest.fn(),
    coordinates: { latitude: 11.95, longitude: 79.83 },
  }),
}));
jest.mock('../api/ridesApi', () => ({
  ridesApi: {
    current: jest.fn(),
    quote: jest.fn(),
    request: jest.fn(),
    cancel: jest.fn(),
    sos: jest.fn(),
    incident: jest.fn(),
    share: jest.fn(),
  },
  incidentCategories: [
    'unsafeDriving',
    'harassment',
    'accident',
    'vehicleProblem',
    'overcharge',
    'other',
  ],
}));

const api = ridesApi as unknown as Record<keyof typeof ridesApi, jest.Mock>;
const moduleStatus = useModuleStatus as jest.Mock;

async function renderScreen(state = signedInState) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <RideScreen />
    </QueryClientProvider>,
    { initialState: state },
  );
}

beforeEach(() => {
  for (const fn of Object.values(api)) {
    fn.mockReset();
  }
  mockGoBack.mockReset();
  moduleStatus.mockReturnValue({ rides: 'available' });
  api.current.mockResolvedValue(null);
});

describe('<RideScreen /> planning', () => {
  it('asks guests to sign in and asks the server nothing', async () => {
    await renderScreen(guestState);

    expect(await screen.findByText('Sign in to book a ride')).toBeOnTheScreen();
    expect(api.current).not.toHaveBeenCalled();
  });

  it('says rides are not open while the module is off, and offers nothing', async () => {
    moduleStatus.mockReturnValue({ rides: 'comingSoon' });
    await renderScreen();

    expect(await screen.findByText(/Rides are not open yet/)).toBeOnTheScreen();
    expect(screen.queryByTestId('rides-see-fares')).toBeNull();
  });

  it('needs a pickup and a different destination before asking for fares', async () => {
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Book a ride');
    expect(screen.getByTestId('rides-see-fares')).toBeDisabled();

    await user.press(screen.getAllByRole('button', { name: 'Promenade Beach' })[0]);
    await user.press(screen.getAllByRole('button', { name: 'Promenade Beach' })[1]);
    expect(screen.getByTestId('rides-see-fares')).toBeDisabled();

    await user.press(screen.getAllByRole('button', { name: 'Auroville (Matrimandir)' })[1]);
    expect(screen.getByTestId('rides-see-fares')).toBeEnabled();
  });

  it('shows the server fares and requests the chosen one with the note', async () => {
    api.quote.mockResolvedValue(
      someQuotes([
        aQuote(),
        aQuote({ quoteId: 'q2', vehicleClass: 'cab', fare: 300, available: false }),
      ]),
    );
    api.request.mockResolvedValue(aRide());
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Book a ride');

    await user.press(screen.getAllByRole('button', { name: 'Promenade Beach' })[0]);
    await user.press(screen.getAllByRole('button', { name: 'Auroville (Matrimandir)' })[1]);
    await user.type(screen.getByLabelText('A note for the driver (optional)'), 'Blue gate');
    await user.press(screen.getByTestId('rides-see-fares'));

    expect(api.quote).toHaveBeenCalledWith(
      expect.objectContaining({ label: 'Promenade Beach' }),
      expect.objectContaining({ label: 'Auroville (Matrimandir)' }),
    );
    expect(
      await screen.findByText(/about 11.5 km · ₹203 paid to the driver in cash/),
    ).toBeOnTheScreen();
    expect(screen.getByText('No drivers are near you right now.')).toBeOnTheScreen();
    expect(screen.getByTestId('rides-request-cab')).toBeDisabled();

    await user.press(screen.getByTestId('rides-request-auto'));

    expect(api.request).toHaveBeenCalledWith(aQuote().quoteId, 'Blue gate');
    expect(await screen.findByText('Looking for a driver')).toBeOnTheScreen();
  });

  it('tells the rider in words when a fare has expired', async () => {
    api.quote.mockResolvedValue(someQuotes());
    api.request.mockRejectedValue(new ApiError(409, 'ride_quote_expired'));
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Book a ride');
    await user.press(screen.getAllByRole('button', { name: 'Promenade Beach' })[0]);
    await user.press(screen.getAllByRole('button', { name: 'Auroville (Matrimandir)' })[1]);
    await user.press(screen.getByTestId('rides-see-fares'));

    await user.press(await screen.findByTestId('rides-request-auto'));

    expect(await screen.findByText('That fare has expired. See fares again.')).toBeOnTheScreen();
  });
});

describe('<RideScreen /> on a ride', () => {
  it('lands on the trip when the app opens in the middle of one, and shows the driver by first name', async () => {
    api.current.mockResolvedValue(withDriver());
    await renderScreen();

    expect(await screen.findByText('Ravi is on the way')).toBeOnTheScreen();
    expect(screen.getByText('Ravi · PY01AB1234')).toBeOnTheScreen();
    expect(screen.queryByTestId('rides-see-fares')).toBeNull();
  });

  it('offers cancelling while searching but not emergency or sharing, and cancels after confirming', async () => {
    api.current.mockResolvedValue(aRide());
    api.cancel.mockResolvedValue(aRide({ status: 'cancelled', cancelledBy: 'rider' }));
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => {
      buttons?.find((b) => b.style === 'destructive')?.onPress?.();
    });
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Looking for a driver');
    expect(screen.queryByTestId('rides-sos')).toBeNull();
    expect(screen.queryByTestId('rides-share')).toBeNull();
    expect(screen.queryByTestId('rides-report')).toBeNull();

    await user.press(screen.getByTestId('rides-cancel'));

    await waitFor(() => expect(api.cancel).toHaveBeenCalledWith(aRide().id));
    expect(await screen.findByText('Ride cancelled')).toBeOnTheScreen();
    alert.mockRestore();
  });

  it('does not offer cancelling once the trip has started', async () => {
    api.current.mockResolvedValue(withDriver({ status: 'inProgress' }));
    await renderScreen();

    expect(await screen.findByText('On your trip')).toBeOnTheScreen();
    expect(screen.queryByTestId('rides-cancel')).toBeNull();
    expect(screen.getByTestId('rides-sos')).toBeOnTheScreen();
  });

  it('sends an emergency alert only after confirming, with where the rider is', async () => {
    api.current.mockResolvedValue(withDriver());
    api.sos.mockResolvedValue({ id: 's1', kind: 'sos', status: 'open', emergencyNumber: '112' });
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Ravi is on the way');

    await user.press(screen.getByTestId('rides-sos'));
    expect(api.sos).not.toHaveBeenCalled();
    expect(alert.mock.calls[0][1]).toContain('call 112 now');

    alert.mock.calls[0][2]?.find((b) => b.style === 'destructive')?.onPress?.();
    await waitFor(() =>
      expect(api.sos).toHaveBeenCalledWith(aRide().id, { latitude: 11.95, longitude: 79.83 }),
    );
    expect(await screen.findByText(/Alert sent/)).toBeOnTheScreen();
    alert.mockRestore();
  });

  it('calls the emergency number from the screen', async () => {
    api.current.mockResolvedValue(withDriver());
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined);
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByRole('button', { name: 'Call 112' }));

    expect(open).toHaveBeenCalledWith('tel:112');
    open.mockRestore();
  });

  it('shares the trip as a link built from the secret the server returned', async () => {
    api.current.mockResolvedValue(withDriver());
    api.share.mockResolvedValue({ token: 'secret-token', expiresAt: '2026-11-04T08:00:00Z' });
    const share = jest
      .spyOn(Share, 'share')
      .mockResolvedValue({ action: 'sharedAction', activityType: undefined });
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Ravi is on the way');

    await user.press(screen.getByTestId('rides-share'));

    await waitFor(() => expect(share).toHaveBeenCalled());
    expect(share.mock.calls[0][0].message).toContain('/trip/secret-token');
    share.mockRestore();
  });

  it('needs a category and some words before it sends a report', async () => {
    api.current.mockResolvedValue(withDriver());
    api.incident.mockResolvedValue({
      id: 'i1',
      kind: 'incident',
      status: 'open',
      emergencyNumber: '112',
    });
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Ravi is on the way');
    await user.press(screen.getByTestId('rides-report'));

    await user.press(screen.getByTestId('rides-report-send'));
    expect(
      screen.getByText('Choose what happened and tell us a little about it.'),
    ).toBeOnTheScreen();
    expect(api.incident).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: 'Overcharge' }));
    await user.type(screen.getByLabelText('Tell us what happened'), 'Asked for more.');
    await user.press(screen.getByTestId('rides-report-send'));

    await waitFor(() =>
      expect(api.incident).toHaveBeenCalledWith(aRide().id, 'overcharge', 'Asked for more.'),
    );
    expect(await screen.findByText(/We have your report/)).toBeOnTheScreen();
  });

  it('tells the rider when a ride ended with no driver', async () => {
    api.current.mockResolvedValue(aRide({ status: 'expired' }));
    await renderScreen();

    expect(await screen.findByText('No driver was available')).toBeOnTheScreen();
    expect(screen.queryByTestId('rides-cancel')).toBeNull();
  });
});
