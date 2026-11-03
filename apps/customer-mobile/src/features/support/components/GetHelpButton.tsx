import { useNavigation } from '@react-navigation/native';
import { StyleSheet, View } from 'react-native';

import { OPButton, useThemedStyles, type Theme } from '../../../design-system';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';

type About =
  { bookingId: string; foodOrderId?: never } | { foodOrderId: string; bookingId?: never };

/**
 * "Get help with this booking or order": starts a request that is about it, so support sees that one thing and not the customer's history.
 * Shown only while support requests are open.
 */
export function GetHelpButton({ about, label }: { about: About; label: string }) {
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const modules = useModuleStatus();
  if (modules.support !== 'available') {
    return null;
  }

  return (
    <View style={styles.row}>
      <OPButton
        label={label}
        variant="tertiary"
        icon="life-buoy"
        onPress={() => navigation.navigate('NewSupportRequest', about)}
      />
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    row: {
      marginTop: theme.spacing.md,
      alignItems: 'flex-start',
    },
  });
