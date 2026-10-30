import { fireEvent, render, screen, userEvent, waitFor } from '@testing-library/react-native';

import type { DriverProfile } from '../features/driver/api/driverApi';
import type { SessionTokens } from '../features/auth/api/authApi';
import { sessionManager } from '../features/auth/services/sessionManager';
import { loadSession } from '../storage/sessionStorage';
import App from './App';

const minutesFromNow = (minutes: number) => new Date(Date.now() + minutes * 60_000).toISOString();

const tokens: SessionTokens = {
  accessToken: 'access-1',
  accessTokenExpiresAt: minutesFromNow(15),
  refreshToken: 'refresh-1',
  refreshTokenExpiresAt: minutesFromNow(30 * 24 * 60),
  user: { id: 'u1', displayName: 'Ravi Kumar', roles: ['customer'] },
  isNewUser: true,
};

const notApplied: DriverProfile = {
  configured: false,
  userId: 'u1',
  fullName: null,
  vehicleType: null,
  vehicleNumber: null,
  licenceNumber: null,
  approvalStatus: null,
  reviewNote: null,
  isOnDuty: false,
  onDutySince: null,
  canGoOnDuty: false,
  version: 0,
};

const pending: DriverProfile = {
  ...notApplied,
  configured: true,
  fullName: 'Ravi Kumar',
  vehicleType: 'scooter',
  vehicleNumber: 'PY01AB1234',
  licenceNumber: 'PY0120200012345',
  approvalStatus: 'pending',
  version: 11,
};

const approved: DriverProfile = {
  ...pending,
  approvalStatus: 'approved',
  canGoOnDuty: true,
  version: 12,
};

type Handler = (body: unknown) => { status: number; body?: unknown };
let routes: Record<string, Handler>;
let requests: { route: string; body: unknown; authorization: string | null }[];

function installFakeApi() {
  requests = [];
  globalThis.fetch = jest.fn((input: string, init: RequestInit) => {
    const route = `${init.method ?? 'GET'} ${new URL(input).pathname}`;
    const body = init.body ? JSON.parse(init.body as string) : undefined;
    const headers = init.headers as Record<string, string>;
    requests.push({ route, body, authorization: headers.Authorization ?? null });
    const handler = routes[route];
    const { status, body: responseBody } = handler ? handler(body) : { status: 404 };
    const text = responseBody === undefined ? '' : JSON.stringify(responseBody);
    return Promise.resolve({
      ok: status >= 200 && status < 300,
      status,
      text: () => Promise.resolve(text),
    });
  }) as unknown as typeof fetch;
}

const requestsTo = (route: string) => requests.filter((request) => request.route === route);

/** Launches the app as someone who is already signed in, with the given driver profile on the server. */
async function launchSignedIn(profile: DriverProfile) {
  routes['GET /api/v1/driver/me'] = () => ({ status: 200, body: profile });
  await sessionManager.start({ ...tokens, isNewUser: false });
  await render(<App />);
}

describe('the driver app', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(async () => {
    await sessionManager.clear();
    routes = { 'POST /api/v1/auth/logout': () => ({ status: 204 }) };
    installFakeApi();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  describe('signing in', () => {
    it('shows the sign-in screen to someone who is signed out', async () => {
      await render(<App />);

      expect(await screen.findByRole('header', { name: 'Drive with OnePondy' })).toBeOnTheScreen();
      expect(screen.getByText('Mobile number')).toBeOnTheScreen();
      expect(requestsTo('GET /api/v1/driver/me')).toHaveLength(0);
    });

    it('checks the number before asking for a code', async () => {
      const user = userEvent.setup();
      await render(<App />);
      await screen.findByText('Mobile number');

      await user.type(screen.getByLabelText('Mobile number'), '12345');
      await user.press(screen.getByRole('button', { name: 'Send code' }));

      expect(await screen.findByText('Enter a 10-digit mobile number.')).toBeOnTheScreen();
      expect(requestsTo('POST /api/v1/auth/otp/request')).toHaveLength(0);
    });

    it('signs a new account in with the texted code and asks it to apply', async () => {
      const user = userEvent.setup();
      routes['POST /api/v1/auth/otp/request'] = () => ({
        status: 200,
        body: {
          challengeId: 'c1',
          expiresAt: minutesFromNow(5),
          resendAfterSeconds: 30,
          maskedDestination: '+91 ******3210',
        },
      });
      routes['POST /api/v1/auth/otp/verify'] = () => ({ status: 200, body: tokens });
      routes['GET /api/v1/driver/me'] = () => ({ status: 200, body: notApplied });
      await render(<App />);
      await screen.findByText('Mobile number');

      await user.type(screen.getByLabelText('Mobile number'), '98765 43210');
      await user.press(screen.getByRole('button', { name: 'Send code' }));
      expect(
        await screen.findByText('We sent a 6-digit code to +91 ******3210.'),
      ).toBeOnTheScreen();

      // The number goes to the API as ten digits, by text message.
      expect(requestsTo('POST /api/v1/auth/otp/request')[0].body).toEqual({
        channel: 'sms',
        destination: '9876543210',
      });

      await user.type(screen.getByLabelText('6-digit code'), '123456');

      expect(await screen.findByRole('header', { name: 'Apply to deliver' })).toBeOnTheScreen();
      expect(requestsTo('POST /api/v1/auth/otp/verify')[0].body).toMatchObject({
        challengeId: 'c1',
        code: '123456',
      });
      // The session is kept for the next launch, and the profile was read with it.
      expect(await loadSession()).toMatchObject({ refreshToken: 'refresh-1' });
      expect(requestsTo('GET /api/v1/driver/me')[0].authorization).toBe('Bearer access-1');
    });

    it('says so when the code is wrong', async () => {
      const user = userEvent.setup();
      routes['POST /api/v1/auth/otp/request'] = () => ({
        status: 200,
        body: {
          challengeId: 'c1',
          expiresAt: minutesFromNow(5),
          resendAfterSeconds: 30,
          maskedDestination: '+91 ******3210',
        },
      });
      routes['POST /api/v1/auth/otp/verify'] = () => ({
        status: 400,
        body: { code: 'invalid_code' },
      });
      await render(<App />);
      await screen.findByText('Mobile number');
      await user.type(screen.getByLabelText('Mobile number'), '9876543210');
      await user.press(screen.getByRole('button', { name: 'Send code' }));
      await screen.findByText('Enter your code', { exact: false });

      await user.type(screen.getByLabelText('6-digit code'), '000000');

      expect(
        await screen.findByText("That code isn't right. Check it and try again."),
      ).toBeOnTheScreen();
      expect(await loadSession()).toBeNull();
    });
  });

  describe('applying', () => {
    it('asks for the vehicle papers only for a motorised vehicle', async () => {
      const user = userEvent.setup();
      await launchSignedIn(notApplied);
      await screen.findByRole('header', { name: 'Apply to deliver' });
      expect(screen.queryByLabelText('Driving licence number')).not.toBeOnTheScreen();

      await user.press(screen.getByRole('button', { name: 'Scooter' }));
      expect(screen.getByLabelText('Driving licence number')).toBeOnTheScreen();
      expect(screen.getByLabelText('Vehicle registration number')).toBeOnTheScreen();

      await user.press(screen.getByRole('button', { name: 'Bicycle' }));
      expect(screen.queryByLabelText('Driving licence number')).not.toBeOnTheScreen();
    });

    it('does not send an incomplete application', async () => {
      const user = userEvent.setup();
      await launchSignedIn(notApplied);
      await screen.findByRole('header', { name: 'Apply to deliver' });

      await user.press(screen.getByRole('button', { name: 'Send application' }));

      expect(
        await screen.findByText('Enter your full name, 2 to 100 characters.'),
      ).toBeOnTheScreen();
      expect(screen.getByText('Choose your vehicle.')).toBeOnTheScreen();

      await user.type(screen.getByLabelText('Full name'), 'Ravi Kumar');
      await user.press(screen.getByRole('button', { name: 'Scooter' }));
      await user.type(screen.getByLabelText('Vehicle registration number'), 'A!');
      await user.type(screen.getByLabelText('Driving licence number'), '123');
      await user.press(screen.getByRole('button', { name: 'Send application' }));

      expect(
        await screen.findByText('Enter the registration number, 4 to 12 letters and digits.'),
      ).toBeOnTheScreen();
      expect(
        screen.getByText('Enter the licence number, 8 to 20 letters and digits.'),
      ).toBeOnTheScreen();
      expect(requestsTo('PUT /api/v1/driver/me')).toHaveLength(0);
    });

    it('sends the application with the numbers tidied, then shows it as in review', async () => {
      const user = userEvent.setup();
      await launchSignedIn(notApplied);
      routes['PUT /api/v1/driver/me'] = () => ({ status: 200, body: pending });
      await screen.findByRole('header', { name: 'Apply to deliver' });

      await user.type(screen.getByLabelText('Full name'), '  Ravi Kumar ');
      await user.press(screen.getByRole('button', { name: 'Scooter' }));
      await user.type(screen.getByLabelText('Vehicle registration number'), 'py 01 ab-1234');
      await user.type(screen.getByLabelText('Driving licence number'), 'py-0120200012345');
      await user.press(screen.getByRole('button', { name: 'Send application' }));

      expect(await screen.findByText('In review')).toBeOnTheScreen();
      expect(requestsTo('PUT /api/v1/driver/me')[0].body).toEqual({
        fullName: 'Ravi Kumar',
        vehicleType: 'scooter',
        vehicleNumber: 'PY01AB1234',
        licenceNumber: 'PY0120200012345',
      });
    });

    it('sends a bicycle with no papers', async () => {
      const user = userEvent.setup();
      await launchSignedIn(notApplied);
      routes['PUT /api/v1/driver/me'] = () => ({
        status: 200,
        body: { ...pending, vehicleType: 'bicycle', vehicleNumber: null, licenceNumber: null },
      });
      await screen.findByRole('header', { name: 'Apply to deliver' });

      await user.type(screen.getByLabelText('Full name'), 'Meena S');
      await user.press(screen.getByRole('button', { name: 'Bicycle' }));
      await user.press(screen.getByRole('button', { name: 'Send application' }));

      await screen.findByText('In review');
      expect(requestsTo('PUT /api/v1/driver/me')[0].body).toEqual({
        fullName: 'Meena S',
        vehicleType: 'bicycle',
        vehicleNumber: null,
        licenceNumber: null,
      });
    });

    it('tells the driver when the licence is already registered', async () => {
      const user = userEvent.setup();
      await launchSignedIn(notApplied);
      routes['PUT /api/v1/driver/me'] = () => ({
        status: 409,
        body: { code: 'driver_licence_in_use' },
      });
      await screen.findByRole('header', { name: 'Apply to deliver' });
      await user.type(screen.getByLabelText('Full name'), 'Ravi Kumar');
      await user.press(screen.getByRole('button', { name: 'Scooter' }));
      await user.type(screen.getByLabelText('Vehicle registration number'), 'PY01AB1234');
      await user.type(screen.getByLabelText('Driving licence number'), 'PY0120200012345');

      await user.press(screen.getByRole('button', { name: 'Send application' }));

      expect(
        await screen.findByText(
          "That licence number can't be used. If you think this is a mistake, contact OnePondy support.",
        ),
      ).toBeOnTheScreen();
      expect(screen.getByRole('header', { name: 'Apply to deliver' })).toBeOnTheScreen();
    });
  });

  describe('waiting for review', () => {
    it('shows the application as in review, with no duty switch, and can check again', async () => {
      const user = userEvent.setup();
      await launchSignedIn(pending);

      expect(await screen.findByText('In review')).toBeOnTheScreen();
      expect(screen.getByRole('header', { name: 'Hi, Ravi' })).toBeOnTheScreen();
      expect(screen.queryByRole('switch')).not.toBeOnTheScreen();

      routes['GET /api/v1/driver/me'] = () => ({ status: 200, body: approved });
      await user.press(screen.getByRole('button', { name: 'Check status' }));

      expect(await screen.findByRole('switch', { name: 'On duty' })).toBeOnTheScreen();
      expect(screen.getByText('Approved')).toBeOnTheScreen();
    });

    it('shows the reviewer reason for a rejection and lets the driver correct and resend', async () => {
      const user = userEvent.setup();
      await launchSignedIn({
        ...pending,
        approvalStatus: 'rejected',
        reviewNote: 'Licence number does not match',
        version: 13,
      });

      expect(
        await screen.findByText('Your application needs changes: Licence number does not match'),
      ).toBeOnTheScreen();

      await user.press(screen.getByRole('button', { name: 'Update your details' }));
      expect(await screen.findByRole('header', { name: 'Your details' })).toBeOnTheScreen();
      expect(screen.getByLabelText('Full name').props.value).toBe('Ravi Kumar');

      routes['PUT /api/v1/driver/me'] = () => ({
        status: 200,
        body: { ...pending, licenceNumber: 'PY0120200099999', version: 14 },
      });
      const licence = screen.getByLabelText('Driving licence number');
      await user.clear(licence);
      await user.type(licence, 'PY0120200099999');
      await user.press(screen.getByRole('button', { name: 'Save changes' }));

      expect(await screen.findByText('In review')).toBeOnTheScreen();
      // The edit says which version it was based on.
      expect(requestsTo('PUT /api/v1/driver/me')[0].body).toMatchObject({
        licenceNumber: 'PY0120200099999',
        version: 13,
      });
    });

    it('shows a suspension with no way to change details', async () => {
      await launchSignedIn({
        ...approved,
        approvalStatus: 'suspended',
        reviewNote: 'Complaint under review.',
        canGoOnDuty: false,
      });

      expect(
        await screen.findByText(
          'Your driver account is suspended: Complaint under review. Contact OnePondy support.',
        ),
      ).toBeOnTheScreen();
      expect(screen.queryByRole('switch')).not.toBeOnTheScreen();
      expect(screen.queryByRole('button', { name: 'Edit details' })).not.toBeOnTheScreen();
    });
  });

  describe('duty', () => {
    it('starts and ends a shift using the version it was looking at', async () => {
      await launchSignedIn(approved);
      const onDuty = {
        ...approved,
        isOnDuty: true,
        onDutySince: '2026-10-30T05:30:00Z',
        version: 13,
      };
      routes['POST /api/v1/driver/duty'] = () => ({ status: 200, body: onDuty });

      expect(await screen.findByText("You're off duty.")).toBeOnTheScreen();
      expect(screen.getByText('Go on duty to be offered deliveries.')).toBeOnTheScreen();

      fireEvent(screen.getByRole('switch', { name: 'On duty' }), 'valueChange', true);

      expect(await screen.findByText(/^You're on duty since /)).toBeOnTheScreen();
      expect(
        screen.getByText("Delivery offers will appear here while you're on duty."),
      ).toBeOnTheScreen();
      expect(requestsTo('POST /api/v1/driver/duty')[0].body).toEqual({ onDuty: true, version: 12 });

      routes['POST /api/v1/driver/duty'] = () => ({
        status: 200,
        body: { ...approved, version: 14 },
      });
      fireEvent(screen.getByRole('switch', { name: 'On duty' }), 'valueChange', false);

      await waitFor(() => expect(requestsTo('POST /api/v1/driver/duty')).toHaveLength(2));
      expect(requestsTo('POST /api/v1/driver/duty')[1].body).toEqual({
        onDuty: false,
        version: 13,
      });
      expect(await screen.findByText('Go on duty to be offered deliveries.')).toBeOnTheScreen();
    });

    it('reloads and explains when the profile changed under the driver', async () => {
      await launchSignedIn(approved);
      routes['POST /api/v1/driver/duty'] = () => ({
        status: 409,
        body: { code: 'driver_changed' },
      });
      await screen.findByRole('switch', { name: 'On duty' });
      const loads = requestsTo('GET /api/v1/driver/me').length;

      fireEvent(screen.getByRole('switch', { name: 'On duty' }), 'valueChange', true);

      expect(
        await screen.findByText(
          "Your details changed on another device. We've loaded the latest, so check them and try again.",
        ),
      ).toBeOnTheScreen();
      await waitFor(() =>
        expect(requestsTo('GET /api/v1/driver/me').length).toBeGreaterThan(loads),
      );
    });

    it('does not collect or send a location', async () => {
      await launchSignedIn(approved);
      routes['POST /api/v1/driver/duty'] = () => ({
        status: 200,
        body: { ...approved, isOnDuty: true, onDutySince: '2026-10-30T05:30:00Z', version: 13 },
      });
      await screen.findByRole('switch', { name: 'On duty' });

      fireEvent(screen.getByRole('switch', { name: 'On duty' }), 'valueChange', true);
      await screen.findByText(/^You're on duty since /);

      expect(JSON.stringify(requests.map((request) => request.body))).not.toMatch(
        /lat|lng|location/i,
      );
    });
  });

  describe('signing out', () => {
    it('ends the session on the server and on the phone, and shows the sign-in screen', async () => {
      const user = userEvent.setup();
      await launchSignedIn(approved);
      await screen.findByRole('switch', { name: 'On duty' });

      await user.press(screen.getByRole('button', { name: 'Sign out' }));

      expect(await screen.findByRole('header', { name: 'Drive with OnePondy' })).toBeOnTheScreen();
      expect(requestsTo('POST /api/v1/auth/logout')).toHaveLength(1);
      expect(await loadSession()).toBeNull();
    });

    it('returns to sign-in with a notice when the server ends the session', async () => {
      routes['POST /api/v1/auth/refresh'] = () => ({
        status: 401,
        body: { code: 'invalid_refresh_token' },
      });
      routes['GET /api/v1/driver/me'] = () => ({ status: 401, body: { code: 'unauthorized' } });
      await sessionManager.start({ ...tokens, isNewUser: false });
      await render(<App />);

      expect(
        await screen.findByText('You were signed out. Sign in again to continue.'),
      ).toBeOnTheScreen();
      expect(await loadSession()).toBeNull();
    });
  });

  describe('when the account cannot load', () => {
    it('offers a retry instead of an empty screen', async () => {
      const user = userEvent.setup();
      routes['GET /api/v1/driver/me'] = () => ({ status: 500 });
      await sessionManager.start({ ...tokens, isNewUser: false });
      await render(<App />);

      // A failed load is retried twice with a growing delay before it is reported.
      expect(
        await screen.findByText("Couldn't load your account", {}, { timeout: 6000 }),
      ).toBeOnTheScreen();

      routes['GET /api/v1/driver/me'] = () => ({ status: 200, body: approved });
      await user.press(screen.getByRole('button', { name: 'Try again' }));

      expect(await screen.findByRole('switch', { name: 'On duty' })).toBeOnTheScreen();
    });
  });
});
