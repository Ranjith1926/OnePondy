import * as Notifications from 'expo-notifications';

import { launchLink, listenForTaps, showNotificationsWhileOpen } from './notificationHandling';

const id = '3f0c2f64-7a5b-4a52-9d58-0d1d4f2d9b11';

function tapOn(data: unknown) {
  return { notification: { request: { content: { data } } } };
}

describe('notification handling', () => {
  afterEach(() => jest.clearAllMocks());

  it('shows a notification that arrives while the app is open, quietly', async () => {
    showNotificationsWhileOpen();

    const { handleNotification } = (Notifications.setNotificationHandler as jest.Mock).mock
      .calls[0][0];
    expect(await handleNotification()).toEqual({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: false,
      shouldSetBadge: false,
    });
  });

  describe('launchLink', () => {
    it('is the booking of the notification that opened the app', () => {
      (Notifications.getLastNotificationResponse as jest.Mock).mockReturnValueOnce(
        tapOn({ type: 'booking.confirmed', bookingId: id }),
      );

      expect(launchLink()).toEqual({ type: 'booking.confirmed', bookingId: id });
    });

    it('is nothing when the app was opened some other way', () => {
      expect(launchLink()).toBeNull();
    });

    it('ignores a notification that does not carry a booking link', () => {
      (Notifications.getLastNotificationResponse as jest.Mock).mockReturnValueOnce(
        tapOn({ type: 'promo.sale', bookingId: id }),
      );

      expect(launchLink()).toBeNull();
    });
  });

  describe('listenForTaps', () => {
    it('passes on a tapped booking notification and ignores any other', () => {
      const onLink = jest.fn();
      listenForTaps(onLink);
      const listener = (Notifications.addNotificationResponseReceivedListener as jest.Mock).mock
        .calls[0][0];

      listener(tapOn({ type: 'booking.accepted', bookingId: id }));
      listener(tapOn({ type: 'booking.accepted', bookingId: 'not-a-uuid' }));

      expect(onLink).toHaveBeenCalledTimes(1);
      expect(onLink).toHaveBeenCalledWith({ type: 'booking.accepted', bookingId: id });
    });

    it('stops listening when told to', () => {
      const remove = jest.fn();
      (Notifications.addNotificationResponseReceivedListener as jest.Mock).mockReturnValueOnce({
        remove,
      });

      listenForTaps(jest.fn())();

      expect(remove).toHaveBeenCalled();
    });
  });
});
