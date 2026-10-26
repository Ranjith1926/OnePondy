import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { LocationHeader, OPIconButton, useThemedStyles, type Theme } from '../../../design-system';
import { useSelectedArea } from '../../location/hooks/useSelectedArea';

interface HomeHeaderProps {
  /** Omitted when the notifications module is hidden. */
  onNotificationsPress?: () => void;
}

export function HomeHeader({ onNotificationsPress }: HomeHeaderProps) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const area = useSelectedArea();

  return (
    <View style={styles.row}>
      <LocationHeader
        caption={t('location.yourArea')}
        areaName={area.name}
        accessibilityHint={t('location.changeHint')}
        onPress={() => navigation.navigate('LocationSelector')}
      />
      {onNotificationsPress ? (
        <OPIconButton
          icon="bell"
          variant="tonal"
          accessibilityLabel={t('home.notifications')}
          onPress={onNotificationsPress}
        />
      ) : null}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
    },
  });
