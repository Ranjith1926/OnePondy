import { useTranslation } from 'react-i18next';
import { View } from 'react-native';

import { OPText } from '../../../design-system';
import type { DeliveryProgress } from '../api/foodApi';

/**
 * Who is bringing the order, in plain words. It names the driver by first name and vehicle; where the driver is stays with OnePondy, so
 * there is nothing here to track on a map.
 */
export function DeliveryLine({ delivery }: { delivery: DeliveryProgress }) {
  const { t } = useTranslation();
  const name = delivery.driverFirstName;

  let text: string | null = null;
  if (delivery.status === 'open') {
    text = t('food.order.delivery.open');
  } else if (delivery.status === 'accepted') {
    text = name
      ? t('food.order.delivery.accepted', { name })
      : t('food.order.delivery.acceptedUnnamed');
  } else if (delivery.status === 'pickedUp') {
    text = name
      ? t('food.order.delivery.pickedUp', { name })
      : t('food.order.delivery.pickedUpUnnamed');
  }

  if (!text) {
    return null;
  }

  return (
    <View accessibilityLiveRegion="polite">
      <OPText variant="bodyStrong">{text}</OPText>
      {delivery.vehicleType && delivery.status !== 'open' ? (
        <OPText tone="secondary">{t(`food.order.delivery.vehicle.${delivery.vehicleType}`)}</OPText>
      ) : null}
    </View>
  );
}
