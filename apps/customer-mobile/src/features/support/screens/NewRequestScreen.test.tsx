import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { ApiError } from '../../../api/apiErrors';
import { bookingId } from '../../../test-utils/bookingFixture';
import { orderId } from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { aTicket, ticketId } from '../../../test-utils/supportFixture';
import { supportApi } from '../api/supportApi';
import { NewRequestScreen } from './NewRequestScreen';

const mockGoBack = jest.fn();
const mockNavigate = jest.fn();
const mockDispatch = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ goBack: mockGoBack, navigate: mockNavigate, dispatch: mockDispatch }),
  StackActions: {
    replace: (name: string, params: unknown) => ({ type: 'REPLACE', name, params }),
  },
}));
jest.mock('../api/supportApi', () => ({
  supportApi: { create: jest.fn() },
  supportTopics: ['booking', 'foodOrder', 'payment', 'account', 'business', 'other'],
}));

const create = supportApi.create as jest.Mock;

async function renderScreen(
  params?: { bookingId?: string; foodOrderId?: string },
  state = signedInState,
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const route = { key: 'k', name: 'NewSupportRequest', params } as never;
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <NewRequestScreen route={route} />
    </QueryClientProvider>,
    { initialState: state },
  );
}

beforeEach(() => {
  create.mockReset();
  mockGoBack.mockReset();
  mockNavigate.mockReset();
  mockDispatch.mockReset();
});

describe('<NewRequestScreen />', () => {
  it('needs a subject and a message before it sends anything', async () => {
    const user = userEvent.setup();
    await renderScreen();

    await user.press(screen.getByTestId('support-submit'));

    expect(screen.getAllByText('This is needed.')).toHaveLength(2);
    expect(create).not.toHaveBeenCalled();
  });

  it('sends the topic, subject and message, then replaces itself with the new request', async () => {
    create.mockResolvedValue(aTicket());
    const user = userEvent.setup();
    await renderScreen();

    await user.press(screen.getByRole('button', { name: 'A payment or refund' }));
    await user.type(screen.getByLabelText('Subject'), '  Refund not received  ');
    await user.type(screen.getByLabelText('Tell us what happened'), 'It has been a week.');
    await user.press(screen.getByTestId('support-submit'));

    expect(create).toHaveBeenCalledWith({
      topic: 'payment',
      subject: 'Refund not received',
      message: 'It has been a week.',
    });
    expect(mockDispatch).toHaveBeenCalledWith({
      type: 'REPLACE',
      name: 'SupportTicket',
      params: { id: ticketId },
    });
  });

  it('links the request to the booking it was started from, and says so', async () => {
    create.mockResolvedValue(aTicket());
    const user = userEvent.setup();
    await renderScreen({ bookingId });

    expect(screen.getByText('Your request will be linked to this booking.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'A booking' })).toBeSelected();
    await user.type(screen.getByLabelText('Subject'), 'Change the date');
    await user.type(screen.getByLabelText('Tell us what happened'), 'Can we move it a day?');
    await user.press(screen.getByTestId('support-submit'));

    expect(create).toHaveBeenCalledWith({
      topic: 'booking',
      subject: 'Change the date',
      message: 'Can we move it a day?',
      bookingId,
    });
  });

  it('links the request to the order it was started from', async () => {
    create.mockResolvedValue(aTicket());
    const user = userEvent.setup();
    await renderScreen({ foodOrderId: orderId });

    expect(screen.getByText('Your request will be linked to this order.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'A food order' })).toBeSelected();
    await user.type(screen.getByLabelText('Subject'), 'Cold food');
    await user.type(screen.getByLabelText('Tell us what happened'), 'It arrived cold.');
    await user.press(screen.getByTestId('support-submit'));

    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ topic: 'foodOrder', foodOrderId: orderId }),
    );
  });

  it('says what went wrong, and stays on the form, when the request is refused', async () => {
    create.mockRejectedValue(new ApiError(409, 'support_too_many_open'));
    const user = userEvent.setup();
    await renderScreen();

    await user.type(screen.getByLabelText('Subject'), 'Another one');
    await user.type(screen.getByLabelText('Tell us what happened'), 'Please help.');
    await user.press(screen.getByTestId('support-submit'));

    expect(await screen.findByText(/already have several open requests/)).toBeOnTheScreen();
    expect(mockDispatch).not.toHaveBeenCalled();
  });

  it('asks a guest to sign in instead of showing the form', async () => {
    await renderScreen(undefined, guestState);

    expect(await screen.findByText('Sign in to ask for help')).toBeOnTheScreen();
    expect(screen.queryByTestId('support-submit')).not.toBeOnTheScreen();
  });
});
