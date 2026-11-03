import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, screen, userEvent, waitFor } from '@testing-library/react-native';
import { Alert } from 'react-native';

import { aProfile, aRide, anOffer, notApplied, rideId } from '../../../test-utils/rideFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { ApiError } from '../../../api/apiErrors';
import { rideDriverApi } from '../api/rideDriverApi';
import { RideHomeScreen } from './RideHomeScreen';

const mockNavigate = jest.fn();
const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate, goBack: mockGoBack }),
}));
jest.mock('../../../session/SessionProvider', () => ({
  useSession: () => ({ status: 'signedIn' }),
}));
jest.mock('../hooks/useRideLive', () => ({ useRideLive: () => ({ connected: true }) }));
jest.mock('../api/rideDriverApi', () => ({
  rideDriverApi: {
    getMine: jest.fn(),
    setDuty: jest.fn(),
    rides: jest.fn(),
    accept: jest.fn(),
    giveUp: jest.fn(),
    arrive: jest.fn(),
    start: jest.fn(),
    complete: jest.fn(),
    cancel: jest.fn(),
    sos: jest.fn(),
    incident: jest.fn(),
    shareLocation: jest.fn(() => Promise.resolve()),
    shareRideLocation: jest.fn(() => Promise.resolve()),
  },
  incidentCategories: [
    'harassment',
    'unsafeDriving',
    'accident',
    'vehicleProblem',
    'overcharge',
    'other',
  ],
}));

const api = rideDriverApi as unknown as Record<string, jest.Mock>;

async function renderScreen() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <RideHomeScreen />
    </QueryClientProvider>,
  );
}

function serve(
  profile = aProfile(),
  rides = { current: null, offers: [], available: true } as object,
) {
  api.getMine.mockResolvedValue(profile);
  api.rides.mockResolvedValue(rides);
}

beforeEach(() => {
  for (const fn of Object.values(api)) {
    fn.mockReset();
  }
  api.shareLocation.mockResolvedValue(undefined);
  api.shareRideLocation.mockResolvedValue(undefined);
  mockNavigate.mockReset();
  mockGoBack.mockReset();
});

describe('<RideHomeScreen /> applying and review', () => {
  it('offers the application to someone who has not applied', async () => {
    serve(notApplied);
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByTestId('ride-start-apply'));

    expect(mockNavigate).toHaveBeenCalledWith('RideApply');
  });

  it('says an application is in review and offers no duty switch', async () => {
    serve(aProfile({ approvalStatus: 'pending', eligible: false }));
    await renderScreen();

    expect(
      await screen.findByText(/checking your licence, permit and insurance/),
    ).toBeOnTheScreen();
    expect(screen.queryByLabelText('On duty for rides')).toBeNull();
    expect(api.rides).not.toHaveBeenCalled();
  });

  it("shows the reviewer's reason for a rejection and a suspension", async () => {
    serve(
      aProfile({
        approvalStatus: 'rejected',
        eligible: false,
        reviewNote: 'Permit photo unreadable',
      }),
    );
    await renderScreen();

    expect(await screen.findByText(/Permit photo unreadable/)).toBeOnTheScreen();
  });

  it('says insurance has ended and does not let the driver go on duty', async () => {
    serve(aProfile({ eligible: false, insuranceEnded: true }));
    await renderScreen();

    expect(await screen.findByText(/Your insurance has ended/)).toBeOnTheScreen();
    expect(screen.getByLabelText('On duty for rides')).toBeDisabled();
  });
});

describe('<RideHomeScreen /> duty and requests', () => {
  it('goes on duty with the version it loaded', async () => {
    serve();
    api.setDuty.mockResolvedValue(aProfile({ isOnDuty: true, version: 4 }));
    await renderScreen();

    fireEvent(await screen.findByLabelText('On duty for rides'), 'valueChange', true);

    await waitFor(() => expect(api.setDuty).toHaveBeenCalledWith(true, 3));
  });

  it('shows requests by the pickup name and distance and accepts one', async () => {
    serve(aProfile({ isOnDuty: true }), { current: null, offers: [anOffer()], available: true });
    api.accept.mockResolvedValue(aRide());
    const user = userEvent.setup();
    await renderScreen();

    expect(await screen.findByText('Promenade Beach → Auroville')).toBeOnTheScreen();
    expect(screen.getByText(/0.4 km away · trip about 11.5 km · ₹203 cash/)).toBeOnTheScreen();

    await user.press(screen.getByTestId(`ride-accept-${rideId}`));

    await waitFor(() => expect(api.accept).toHaveBeenCalledWith(rideId));
  });

  it('tells the driver who lost the race that the ride was taken', async () => {
    serve(aProfile({ isOnDuty: true }), { current: null, offers: [anOffer()], available: true });
    api.accept.mockRejectedValue(new ApiError(409, 'ride_taken'));
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByTestId(`ride-accept-${rideId}`));

    expect(await screen.findByText('Another driver took this ride.')).toBeOnTheScreen();
  });

  it('keeps the duty switch locked while a ride is held', async () => {
    serve(aProfile({ isOnDuty: true }), { current: aRide(), offers: [], available: true });
    await renderScreen();

    expect(await screen.findByText('Go to Anita')).toBeOnTheScreen();
    expect(screen.getByLabelText('On duty for rides')).toBeDisabled();
  });
});

describe('<RideHomeScreen /> on a ride', () => {
  const onRide = (overrides = {}) =>
    serve(aProfile({ isOnDuty: true }), { current: aRide(overrides), offers: [], available: true });

  it("shows the exact pickup, the note and the rider's first name once accepted", async () => {
    onRide();
    await renderScreen();

    expect(await screen.findByText('Pickup: Promenade Beach')).toBeOnTheScreen();
    expect(screen.getByText('Note: Blue gate')).toBeOnTheScreen();
    expect(screen.getByText('Drop: Auroville')).toBeOnTheScreen();
  });

  it('marks arrival, and can give the ride up before arriving', async () => {
    onRide();
    api.arrive.mockResolvedValue(aRide({ status: 'arrived' }));
    api.giveUp.mockResolvedValue(aRide());
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByTestId('ride-step-arrive'));
    await waitFor(() => expect(api.arrive).toHaveBeenCalledWith(rideId));

    await user.press(screen.getByTestId('ride-give-up'));
    await waitFor(() => expect(api.giveUp).toHaveBeenCalledWith(rideId));
  });

  it('starts the trip once arrived, and can end the ride only with a reason', async () => {
    onRide({ status: 'arrived' });
    api.start.mockResolvedValue(aRide({ status: 'inProgress' }));
    api.cancel.mockResolvedValue(aRide({ status: 'cancelled' }));
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Waiting for Anita');
    expect(screen.queryByTestId('ride-give-up')).toBeNull();

    await user.press(screen.getByTestId('ride-step-start'));
    await waitFor(() => expect(api.start).toHaveBeenCalledWith(rideId));

    await user.press(screen.getByTestId('ride-end'));
    expect(screen.getByTestId('ride-end-confirm')).toBeDisabled();
    await user.type(screen.getByLabelText('Why can you not go on?'), 'Rider did not come');
    await user.press(screen.getByTestId('ride-end-confirm'));
    await waitFor(() => expect(api.cancel).toHaveBeenCalledWith(rideId, 'Rider did not come'));
  });

  it('ends the trip only after confirming the exact cash, and sends the fare', async () => {
    onRide({ status: 'inProgress' });
    api.complete.mockResolvedValue(aRide({ status: 'completed' }));
    const alert = jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
      buttons?.find((b) => b.style !== 'cancel')?.onPress?.();
    });
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Trip with Anita');
    expect(screen.queryByTestId('ride-give-up')).toBeNull();
    expect(screen.queryByTestId('ride-end')).toBeNull();

    await user.press(screen.getByTestId('ride-step-complete'));

    expect(alert.mock.calls[0][1]).toContain('₹203');
    await waitFor(() => expect(api.complete).toHaveBeenCalledWith(rideId, 203));
    alert.mockRestore();
  });

  it('sends an emergency alert only after confirming', async () => {
    onRide();
    api.sos.mockResolvedValue({ id: 's1', kind: 'sos', status: 'open', emergencyNumber: '112' });
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Go to Anita');

    await user.press(screen.getByTestId('ride-sos'));
    expect(api.sos).not.toHaveBeenCalled();
    alert.mock.calls[0][2]?.find((b) => b.style === 'destructive')?.onPress?.();

    await waitFor(() => expect(api.sos).toHaveBeenCalledWith(rideId, null));
    expect(await screen.findByText(/Alert sent/)).toBeOnTheScreen();
    alert.mockRestore();
  });

  it('needs a category and some words before it sends a report', async () => {
    onRide();
    api.incident.mockResolvedValue({
      id: 'i1',
      kind: 'incident',
      status: 'open',
      emergencyNumber: '112',
    });
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByText('Go to Anita');
    await user.press(screen.getByTestId('ride-report'));

    await user.press(screen.getByTestId('ride-report-send'));
    expect(
      screen.getByText('Choose what happened and tell us a little about it.'),
    ).toBeOnTheScreen();
    expect(api.incident).not.toHaveBeenCalled();

    await user.press(screen.getByRole('button', { name: 'Harassment' }));
    await user.type(screen.getByLabelText('Tell us what happened'), 'Rider was abusive.');
    await user.press(screen.getByTestId('ride-report-send'));

    await waitFor(() =>
      expect(api.incident).toHaveBeenCalledWith(rideId, 'harassment', 'Rider was abusive.'),
    );
  });
});
