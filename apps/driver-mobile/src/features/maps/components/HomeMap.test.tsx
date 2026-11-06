import { act, screen, userEvent, waitFor } from '@testing-library/react-native';
import * as Location from 'expo-location';

import { renderWithProviders } from '../../../test-utils/render';
import { HomeMap } from './HomeMap';

const watch = Location.watchPositionAsync as jest.Mock;
const getPermission = Location.getForegroundPermissionsAsync as jest.Mock;
const requestPermission = Location.requestForegroundPermissionsAsync as jest.Mock;

beforeEach(() => {
  watch.mockReset();
  watch.mockImplementation(() => Promise.resolve({ remove: jest.fn() }));
  getPermission.mockReset();
  getPermission.mockResolvedValue({ status: 'granted' });
  requestPermission.mockReset();
  requestPermission.mockResolvedValue({ status: 'granted' });
});

describe('<HomeMap />', () => {
  it('shows the driver on the map once the phone knows where they are, and says it stays on the phone', async () => {
    await renderWithProviders(<HomeMap />);

    expect(screen.getByLabelText('Map showing where you are')).toBeOnTheScreen();
    expect(screen.queryByTestId('map-marker-me')).not.toBeOnTheScreen();
    await waitFor(() => expect(watch).toHaveBeenCalledTimes(1));
    const report = watch.mock.calls[0][1] as (reading: unknown) => void;
    await act(async () => report({ coords: { latitude: 11.93, longitude: 79.83 } }));

    expect(await screen.findByTestId('map-marker-me')).toBeOnTheScreen();
    expect(screen.getByText(/drawn on this phone only/)).toBeOnTheScreen();
  });

  it('asks for location only when the driver chooses to show their position', async () => {
    getPermission.mockResolvedValue({ status: 'undetermined' });
    await renderWithProviders(<HomeMap />);

    const button = await screen.findByRole('button', { name: 'Show where I am on the map' });
    expect(requestPermission).not.toHaveBeenCalled();
    expect(watch).not.toHaveBeenCalled();

    await userEvent.setup().press(button);

    await waitFor(() => expect(watch).toHaveBeenCalledTimes(1));
    expect(requestPermission).toHaveBeenCalledTimes(1);
  });

  it('keeps the map of the town and explains when location is refused', async () => {
    getPermission.mockResolvedValue({ status: 'undetermined' });
    requestPermission.mockResolvedValue({ status: 'denied' });
    await renderWithProviders(<HomeMap />);

    await userEvent
      .setup()
      .press(await screen.findByRole('button', { name: 'Show where I am on the map' }));

    expect(await screen.findByText(/Location is off/)).toBeOnTheScreen();
    expect(screen.getByLabelText('Map showing where you are')).toBeOnTheScreen();
    expect(watch).not.toHaveBeenCalled();
  });
});
