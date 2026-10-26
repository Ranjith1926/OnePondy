import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPAvatar, OPButton, OPText, useThemedStyles, type Theme } from '../../../design-system';

interface ProfileSummaryProps {
  onSignIn: () => void;
}

export function ProfileSummary({ onSignIn }: ProfileSummaryProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.card}>
      <View style={styles.row}>
        <OPAvatar />
        <View style={styles.text}>
          <OPText variant="heading">{t('profile.guestName')}</OPText>
          <OPText variant="caption" tone="secondary">
            {t('profile.guestMessage')}
          </OPText>
        </View>
      </View>
      <OPButton label={t('profile.signIn')} onPress={onSignIn} icon="log-in" fullWidth />
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    card: {
      gap: theme.spacing.md,
      marginHorizontal: theme.layout.screenPadding,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.md,
    },
    text: {
      flex: 1,
      gap: 2,
    },
  });
