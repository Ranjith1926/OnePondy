import { useTranslation } from 'react-i18next';

import type { BookingDetail } from '../../bookings/api/bookingsApi';
import { bookingErrorMessageKey } from '../../bookings/bookingErrors';
import { usePayBooking } from '../hooks/usePayBooking';
import { PaymentPanel } from './PaymentPanel';

interface Props {
  booking: BookingDetail;
}

/** Shown while the partner has accepted a booking and the customer still has to pay. */
export function PaymentCard({ booking }: Props) {
  const { t } = useTranslation();
  const pay = usePayBooking(booking.id, booking.listingTitle);

  return (
    <PaymentPanel
      amount={booking.priceSnapshot.totalAmount}
      currency={booking.priceSnapshot.currency}
      expiresAt={booking.expiresAt}
      pay={pay}
      messages={{
        accepted: t('payments.pay.accepted'),
        timeUp: t('payments.pay.timeUp'),
        lapsed: t('payments.outcome.lapsed'),
      }}
      errorMessage={(error) => t(bookingErrorMessageKey(error))}
    />
  );
}
