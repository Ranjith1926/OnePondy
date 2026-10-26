import Constants from 'expo-constants';
import { useTranslation } from 'react-i18next';
import { Linking, Platform, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  OPButton,
  OPIcon,
  OPText,
  useTheme,
  useThemedStyles,
  type Theme,
} from '../../../design-system';

/** Play Store listing for this app. iOS has no App Store listing yet (spec D4, D17). */
function storeUrl(): string | null {
  const androidPackage = Constants.expoConfig?.android?.package;
  return Platform.OS === 'android' && androidPackage
    ? `https://play.google.com/store/apps/details?id=${androidPackage}`
    : null;
}

/** Shown instead of the app when this version is older than the server's minimum (spec §8). */
export function UpdateRequiredScreen() {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const url = storeUrl();

  return (
    <SafeAreaView style={styles.screen}>
      <View style={styles.body}>
        <OPIcon name="circle-arrow-up" size={48} color={theme.colors.brand.primary} />
        <OPText variant="title" align="center" accessibilityRole="header">
          {t('updateRequired.title')}
        </OPText>
        <OPText tone="secondary" align="center">
          {t('updateRequired.message')}
        </OPText>
        {url ? (
          <OPButton
            label={t('updateRequired.action')}
            onPress={() => void Linking.openURL(url)}
            fullWidth
            size="lg"
          />
        ) : null}
      </View>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    body: {
      flex: 1,
      alignItems: 'center',
      justifyContent: 'center',
      gap: theme.spacing.md,
      padding: theme.layout.screenPadding,
    },
  });
