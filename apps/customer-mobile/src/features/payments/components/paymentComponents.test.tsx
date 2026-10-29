import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent, waitFor } from '@testing-library/react-native';
import type { ReactElement } from 'react';

import { aBooking } from '../../../test-utils/bookingFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { paymentsApi } from '../api/paymentsApi';
import { openRazorpayCheckout } from '../checkout/razorpayCheckout';
import { PaymentCard } from './PaymentCard';
import { ReceiptSection } from './ReceiptSection';

jest.mock('../api/paymentsApi', () => ({ paymentsApi: { start: jest.fn(), refresh: jest.fn() } }));
jest.mock('../checkout/razorpayCheckout', () => ({ openRazorpayCheckout: jest.fn() }));

const start = paymentsApi.start as jest.Mock;
const refresh = paymentsApi.refresh as jest.Mock;
const checkout = openRazorpayCheckout as jest.Mock;

function renderWithQueries(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return renderWithProviders(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

/** A booking that still has twenty minutes to pay. */
const inTime = () => aBooking({ expiresAt: new Date(Date.now() + 20 * 60_000).toISOString() });

function aPaymentStart() {
  return {
    bookingId: 'b',
    attemptId: 'a',
    provider: 'razorpay',
    mode: 'sandbox',
    keyId: 'rzp_test_key',
    orderId: 'order_1',
    amount: 1000,
    amountPaise: 100000,
    currency: 'INR',
    paymentExpiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
  };
}

describe('<PaymentCard />', () => {
  beforeEach(() => {
    start.mockReset();
    refresh.mockReset();
    checkout.mockReset();
  });

  it('asks to pay the amount, with the time left', async () => {
    await renderWithQueries(<PaymentCard booking={inTime()} />);

    expect(screen.getByRole('button', { name: /Pay .*1,000/ })).toBeOnTheScreen();
    expect(screen.getByText(/Time left to pay: \d+:\d\d/)).toBeOnTheScreen();
  });

  it('cannot be paid once the time has run out', async () => {
    await renderWithQueries(
      <PaymentCard booking={aBooking({ expiresAt: new Date(Date.now() - 1_000).toISOString() })} />,
    );

    expect(screen.getByText(/time to pay has run out/i)).toBeOnTheScreen();
    expect(screen.getByTestId('pay-button')).toBeDisabled();
  });

  it('opens the checkout and asks the server once the customer has paid', async () => {
    start.mockResolvedValue(aPaymentStart());
    checkout.mockResolvedValue({ kind: 'completed' });
    refresh.mockResolvedValue(aBooking({ status: 'confirmed' }));

    await renderWithQueries(<PaymentCard booking={inTime()} />);
    await userEvent.setup().press(screen.getByTestId('pay-button'));

    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(checkout).toHaveBeenCalledWith(
      expect.objectContaining({ orderId: 'order_1', keyId: 'rzp_test_key' }),
    );
  });

  it('says so, and lets the customer try again, when they close the checkout', async () => {
    start.mockResolvedValue(aPaymentStart());
    checkout.mockResolvedValue({ kind: 'cancelled' });
    refresh.mockResolvedValue(aBooking({ status: 'awaitingPayment' }));

    await renderWithQueries(<PaymentCard booking={inTime()} />);
    await userEvent.setup().press(screen.getByTestId('pay-button'));

    expect(await screen.findByText(/Payment wasn't completed/)).toBeOnTheScreen();
    expect(screen.getByTestId('pay-button')).toBeEnabled();
  });

  it('explains when the server refuses to start a payment', async () => {
    const { ApiError } = jest.requireActual('../../../api/apiErrors');
    start.mockRejectedValue(new ApiError(409, 'payment_not_due'));

    await renderWithQueries(<PaymentCard booking={inTime()} />);
    await userEvent.setup().press(screen.getByTestId('pay-button'));

    expect(await screen.findByText(/isn't waiting for payment/)).toBeOnTheScreen();
    expect(checkout).not.toHaveBeenCalled();
  });
});

describe('<ReceiptSection />', () => {
  it('shows nothing for a booking that was never paid', async () => {
    await renderWithProviders(<ReceiptSection booking={{ payment: null, refunds: [] }} />);

    expect(screen.queryByTestId('receipt')).not.toBeOnTheScreen();
    expect(screen.queryByTestId('refund')).not.toBeOnTheScreen();
  });

  it('shows what was paid and its reference', async () => {
    await renderWithProviders(
      <ReceiptSection
        booking={{
          payment: {
            amount: 1000,
            currency: 'INR',
            paidAt: '2026-10-29T10:10:00Z',
            reference: 'pay_ABC123',
          },
          refunds: [],
        }}
      />,
    );

    expect(screen.getByTestId('receipt')).toBeOnTheScreen();
    expect(screen.getByText(/Paid .*1,000/)).toBeOnTheScreen();
    expect(screen.getByText('Payment reference: pay_ABC123')).toBeOnTheScreen();
  });

  it.each(['pending', 'failed'] as const)('says a %s refund is on its way', async (status) => {
    await renderWithProviders(
      <ReceiptSection
        booking={{
          payment: null,
          refunds: [
            {
              amount: 500,
              reason: 'customerCancelled',
              status,
              requestedAt: '2026-10-30T09:00:00Z',
              processedAt: null,
            },
          ],
        }}
      />,
    );

    expect(screen.getByText(/refund of .*500.* is on its way/)).toBeOnTheScreen();
    expect(screen.getByText(/let you know when it's sent/)).toBeOnTheScreen();
  });

  it('says when a refund was sent', async () => {
    await renderWithProviders(
      <ReceiptSection
        booking={{
          payment: null,
          refunds: [
            {
              amount: 1000,
              reason: 'partnerCancelled',
              status: 'processed',
              requestedAt: '2026-10-30T09:00:00Z',
              processedAt: '2026-10-30T09:05:00Z',
            },
          ],
        }}
      />,
    );

    expect(screen.getByText(/refund of .*1,000.* was sent on/)).toBeOnTheScreen();
    expect(screen.getByText(/bank may take a few working days/)).toBeOnTheScreen();
  });
});
