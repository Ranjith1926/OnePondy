import { useTranslation } from 'react-i18next';
import { Linking, View } from 'react-native';

import { OPButton, OPText } from '../../../design-system';
import { AppMap } from '../../maps/components/AppMap';
import type { DeliveryProgress } from '../api/foodApi';

/** "10:20 am": when the driver's position was last reported, in the device's language and time zone. */
function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
}

/**
 * Who is bringing the order, in plain words: the driver's first name and vehicle. Once the driver has collected the order, and
 * has reported where they are, the customer can open that spot in their Maps app.
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

  const position = delivery.status === 'pickedUp' ? delivery.position : null;

  return (
    <View accessibilityLiveRegion="polite">
      <OPText variant="bodyStrong">{text}</OPText>
      {delivery.vehicleType && delivery.status !== 'open' ? (
        <OPText tone="secondary">{t(`food.order.delivery.vehicle.${delivery.vehicleType}`)}</OPText>
      ) : null}
      {position ? (
        <>
          {delivery.locationUpdatedAt ? (
            <OPText tone="secondary">
              {t('food.order.delivery.updated', { time: formatTime(delivery.locationUpdatedAt) })}
            </OPText>
          ) : null}
          <AppMap
            markers={[
              {
                id: 'driver',
                latitude: position.latitude,
                longitude: position.longitude,
                label: t('food.order.delivery.mapPin'),
                tone: 'accent',
              },
            ]}
            accessibilityLabel={t('food.order.delivery.mapLabel')}
            height={220}
            // Each new report from the driver starts a fresh view centred on them.
            viewKey={`${position.latitude},${position.longitude}`}
          />
          <OPButton
            label={t('food.order.delivery.seeOnMap')}
            variant="secondary"
            onPress={() =>
              void Linking.openURL(
                `https://www.google.com/maps/search/?api=1&query=${position.latitude},${position.longitude}`,
              )
            }
          />
        </>
      ) : null}
    </View>
  );
}
