import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { ApiError } from '../../../api/apiErrors';
import { renderWithProviders } from '../../../test-utils/render';
import { signedInState } from '../../../test-utils/signedIn';
import { aTicket, aTicketMessage, ticketId } from '../../../test-utils/supportFixture';
import { supportApi } from '../api/supportApi';
import { TicketScreen } from './TicketScreen';

const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => {
  const { useEffect } = jest.requireActual('react');
  return {
    useNavigation: () => ({ goBack: mockGoBack }),
    // A screen is focused as soon as it is shown.
    useFocusEffect: (effect: () => void) => useEffect(effect, [effect]),
  };
});
jest.mock('../api/supportApi', () => ({ supportApi: { get: jest.fn(), reply: jest.fn() } }));

const get = supportApi.get as jest.Mock;
const reply = supportApi.reply as jest.Mock;

async function renderScreen() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const route = { key: 'k', name: 'SupportTicket', params: { id: ticketId } } as never;
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <TicketScreen route={route} />
    </QueryClientProvider>,
    { initialState: signedInState },
  );
}

beforeEach(() => {
  get.mockReset();
  reply.mockReset();
  mockGoBack.mockReset();
});

describe('<TicketScreen />', () => {
  it('shows the request, its reference and where it stands', async () => {
    get.mockResolvedValue(aTicket());

    await renderScreen();

    expect(await screen.findByText('Where is my order?')).toBeOnTheScreen();
    expect(screen.getByText('Reference D0000001')).toBeOnTheScreen();
    expect(screen.getByText('Waiting for OnePondy')).toBeOnTheScreen();
  });

  it('shows the conversation, with support’s messages told apart and carrying no staff name', async () => {
    get.mockResolvedValue(
      aTicket({
        status: 'waitingForRequester',
        messages: [
          aTicketMessage(),
          aTicketMessage({ id: 'm2', fromSupport: true, body: 'Looking into it now.' }),
        ],
      }),
    );

    await renderScreen();

    expect(await screen.findByText('Looking into it now.')).toBeOnTheScreen();
    expect(screen.getAllByTestId('message-you')).toHaveLength(1);
    expect(screen.getAllByTestId('message-support')).toHaveLength(1);
    expect(screen.getByText(/^OnePondy support · /)).toBeOnTheScreen();
    expect(screen.getByText('OnePondy has replied')).toBeOnTheScreen();
  });

  it('shows the booking or order it is about in a few lines', async () => {
    get.mockResolvedValue(
      aTicket({
        about: {
          kind: 'food_order',
          id: 'o1',
          title: 'Spice Route',
          status: 'delivered',
          totalAmount: 339.25,
          currency: 'INR',
          when: '2026-11-02T10:00:00Z',
        },
      }),
    );

    await renderScreen();

    expect(await screen.findByTestId('about')).toBeOnTheScreen();
    expect(screen.getByText('About this order')).toBeOnTheScreen();
    expect(screen.getByText('Spice Route')).toBeOnTheScreen();
  });

  it('sends a reply, and needs words to do it', async () => {
    get.mockResolvedValue(aTicket());
    reply.mockResolvedValue(aTicket());
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByTestId('support-send'));
    expect(screen.getByText('This is needed.')).toBeOnTheScreen();
    expect(reply).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText('Your reply'), '  Thank you.  ');
    await user.press(screen.getByTestId('support-send'));

    expect(reply).toHaveBeenCalledWith(ticketId, 'Thank you.');
  });

  it('says what went wrong when a reply is refused', async () => {
    get.mockResolvedValue(aTicket());
    reply.mockRejectedValue(new ApiError(409, 'support_too_many_messages'));
    const user = userEvent.setup();
    await renderScreen();

    await user.type(await screen.findByLabelText('Your reply'), 'One more thing.');
    await user.press(screen.getByTestId('support-send'));

    expect(await screen.findByText(/a lot of messages already/)).toBeOnTheScreen();
  });

  it('says a resolved request can be reopened by replying', async () => {
    get.mockResolvedValue(aTicket({ status: 'resolved' }));

    await renderScreen();

    expect(
      await screen.findByText(/This request is resolved\. If it isn't, reply/),
    ).toBeOnTheScreen();
    expect(screen.getByTestId('support-send')).toBeOnTheScreen();
  });

  it('offers no reply once it was resolved too long ago, and says to start a new one', async () => {
    get.mockResolvedValue(aTicket({ status: 'resolved', canReply: false }));

    await renderScreen();

    expect(
      await screen.findByText('This request was resolved a while ago. Please start a new one.'),
    ).toBeOnTheScreen();
    expect(screen.queryByTestId('support-send')).not.toBeOnTheScreen();
  });

  it('says so, and lets the customer retry, when the request cannot be loaded', async () => {
    get.mockRejectedValueOnce(new Error('offline'));
    await renderScreen();
    expect(await screen.findByText("Couldn't load this request")).toBeOnTheScreen();

    get.mockResolvedValueOnce(aTicket());
    await userEvent.setup().press(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Where is my order?')).toBeOnTheScreen();
  });
});
