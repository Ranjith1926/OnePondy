import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { signedInState } from '../../../test-utils/signedIn';
import { itinerariesApi } from '../api/itinerariesApi';
import { AddToItinerarySheet } from './AddToItinerarySheet';

jest.mock('../api/itinerariesApi', () => ({
  itinerariesApi: { list: jest.fn(), create: jest.fn(), addStop: jest.fn() },
}));

const list = itinerariesApi.list as jest.Mock;
const create = itinerariesApi.create as jest.Mock;
const addStop = itinerariesApi.addStop as jest.Mock;

const placeId = 'place-promenade';

const summary = (id: string, title: string) => ({
  id,
  title,
  startDate: null,
  endDate: null,
  status: 'saved',
  source: 'manual',
  isShared: false,
  stopCount: 0,
  createdAt: '2026-11-05T05:00:00Z',
  updatedAt: '2026-11-05T05:00:00Z',
});

async function renderSheet(onAdded = jest.fn(), onClose = jest.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <AddToItinerarySheet visible placeId={placeId} onClose={onClose} onAdded={onAdded} />
    </QueryClientProvider>,
    { initialState: signedInState },
  );
  return { onAdded, onClose };
}

beforeEach(() => {
  list.mockReset();
  create.mockReset();
  addStop.mockReset();
  list.mockResolvedValue([]);
  addStop.mockResolvedValue({});
});

describe('<AddToItinerarySheet />', () => {
  it('adds the place to the trip it has just created, not to a trip named after the place', async () => {
    create.mockResolvedValue({ id: 'trip-new', title: 'Weekend in Pondy' });
    const user = userEvent.setup();
    const { onAdded } = await renderSheet();

    await user.press(await screen.findByRole('button', { name: 'Start a new trip' }));
    await user.type(screen.getByLabelText('Trip name'), 'Weekend in Pondy');
    await user.press(screen.getByRole('button', { name: 'Create and add' }));

    expect(create).toHaveBeenCalledWith({ title: 'Weekend in Pondy' }, expect.any(String));
    expect(addStop).toHaveBeenCalledTimes(1);
    expect(addStop.mock.calls[0][0]).toBe('trip-new');
    expect(addStop.mock.calls[0][1]).toEqual({ placeId });
    expect(onAdded).toHaveBeenCalledWith('Weekend in Pondy');
  });

  it('adds the place to the trip the customer picks from their list', async () => {
    list.mockResolvedValue([summary('trip-a', 'Beach days'), summary('trip-b', 'Heritage walk')]);
    const user = userEvent.setup();
    const { onAdded } = await renderSheet();

    await user.press(await screen.findByText('Heritage walk'));

    expect(addStop.mock.calls[0][0]).toBe('trip-b');
    expect(addStop.mock.calls[0][1]).toEqual({ placeId });
    expect(onAdded).toHaveBeenCalledWith('Heritage walk');
  });

  it('says so, and stays open, when the place cannot be added', async () => {
    list.mockResolvedValue([summary('trip-a', 'Beach days')]);
    addStop.mockRejectedValue(new Error('offline'));
    const user = userEvent.setup();
    const { onAdded, onClose } = await renderSheet();

    await user.press(await screen.findByText('Beach days'));

    expect(await screen.findByText('Something went wrong. Try again.')).toBeOnTheScreen();
    expect(onAdded).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });
});
