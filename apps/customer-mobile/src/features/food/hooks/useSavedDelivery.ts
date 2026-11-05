import { useEffect, useState } from 'react';

import { loadDeliveryDetails, type DeliveryDetails } from '../../../storage/deliveryDetailsStorage';

/** The delivery address and phone this user gave for their last order, read once from the phone. */
export function useSavedDelivery(userId: string | undefined): DeliveryDetails | null {
  const [saved, setSaved] = useState<DeliveryDetails | null>(null);

  useEffect(() => {
    if (userId === undefined) {
      return;
    }
    let active = true;
    loadDeliveryDetails(userId)
      .then((details) => {
        if (active) setSaved(details);
      })
      .catch(() => {
        // Storage unavailable: the customer types them as before.
      });
    return () => {
      active = false;
    };
  }, [userId]);

  return saved;
}
