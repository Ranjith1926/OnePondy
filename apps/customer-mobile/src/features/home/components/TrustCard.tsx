import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPIcon, OPText, useTheme, useThemedStyles, type Theme } from '../../../design-system';

/** Explains the verification promise behind the content (spec §1 product principles). */
export function TrustCard() {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);

  return (
    <View style={styles.card}>
      <OPIcon name="shield-check" size={24} color={theme.colors.success} />
      <View style={styles.text}>
        <OPText variant="bodyStrong" accessibilityRole="header">
          {t('home.trust.title')}
        </OPText>
        <OPText variant="caption" tone="secondary">
          {t('home.trust.body')}
        </OPText>
      </View>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    card: {
      flexDirection: 'row',
      alignItems: 'flex-start',
      gap: theme.spacing.sm,
      marginHorizontal: theme.layout.screenPadding,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      backgroundColor: theme.colors.successContainer,
    },
    text: {
      flex: 1,
      gap: theme.spacing.xxs,
    },
  });
