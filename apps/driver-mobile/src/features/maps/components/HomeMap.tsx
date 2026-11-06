import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPButton, OPText, useThemedStyles, type Theme } from '../../../design-system';
import { useDevicePosition } from '../hooks/useDevicePosition';
import { AppMap } from './AppMap';

/** Puducherry, shown until the phone says where the driver is. */
const town = { latitude: 11.9416, longitude: 79.8083 };

/**
 * The map at the top of the driver's home: where they are, drawn on this phone only. Nothing here is sent to OnePondy (that happens
 * only during a delivery, in the deliveries section), and nothing is asked of the driver until they choose to show their position.
 */
export function HomeMap() {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const { position, permission, ask } = useDevicePosition();

  return (
    <View style={styles.box}>
      <AppMap
        markers={
          position ? [{ id: 'me', ...position, label: t('home.mapYou'), tone: 'accent' }] : []
        }
        accessibilityLabel={t('home.mapLabel')}
        height={220}
        center={town}
        zoom={13}
        viewKey={position ? 'located' : 'town'}
      />
      {permission === 'granted' && !position ? (
        <OPText variant="caption" tone="secondary">
          {t('home.mapLocating')}
        </OPText>
      ) : null}
      {permission === 'unknown' ? (
        <OPButton label={t('home.mapShowMe')} variant="secondary" onPress={ask} />
      ) : null}
      {permission === 'denied' ? (
        <OPText variant="caption" tone="secondary">
          {t('home.mapDenied')}
        </OPText>
      ) : null}
      <OPText variant="caption" tone="secondary">
        {t('home.mapNote')}
      </OPText>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    box: {
      gap: theme.spacing.xs,
    },
  });
