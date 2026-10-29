import * as Notifications from 'expo-notifications';

import { bookingLinkFrom, type BookingLink } from './notificationLinks';

/** A notification that arrives while the app is open is shown as a banner, quietly: the customer is already looking at the app. */
export function showNotificationsWhileOpen(): void {
  Notifications.setNotificationHandler({
    handleNotification: () =>
      Promise.resolve({
        shouldShowBanner: true,
        shouldShowList: true,
        shouldPlaySound: false,
        shouldSetBadge: false,
      }),
  });
}

function linkOf(response: Notifications.NotificationResponse | null): BookingLink | null {
  return response ? bookingLinkFrom(response.notification.request.content.data) : null;
}

/** The notification that started the app from closed, if that is how it was opened. */
export function launchLink(): BookingLink | null {
  return linkOf(Notifications.getLastNotificationResponse());
}

/** Calls `onLink` whenever a notification is tapped while the app is running. Returns how to stop listening. */
export function listenForTaps(onLink: (link: BookingLink) => void): () => void {
  const subscription = Notifications.addNotificationResponseReceivedListener((response) => {
    const link = linkOf(response);
    if (link) {
      onLink(link);
    }
  });
  return () => subscription.remove();
}
