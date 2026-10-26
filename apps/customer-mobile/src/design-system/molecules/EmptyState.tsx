import { StyleSheet, View } from 'react-native';

import { OPButton } from '../atoms/OPButton';
import { OPIcon, type IconName } from '../atoms/OPIcon';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface EmptyStateProps {
  icon: IconName;
  title: string;
  message: string;
  actionLabel?: string;
  onAction?: () => void;
}

export function EmptyState({ icon, title, message, actionLabel, onAction }: EmptyStateProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.container}>
      <View style={styles.iconCircle}>
        <OPIcon name={icon} size={30} color={theme.colors.brand.onPrimaryContainer} />
      </View>
      <OPText variant="heading" align="center" accessibilityRole="header">
        {title}
      </OPText>
      <OPText tone="secondary" align="center">
        {message}
      </OPText>
      {actionLabel && onAction ? (
        <View style={styles.action}>
          <OPButton label={actionLabel} onPress={onAction} variant="secondary" />
        </View>
      ) : null}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    container: {
      alignItems: 'center',
      gap: theme.spacing.xs,
      paddingHorizontal: theme.spacing.xl,
      paddingVertical: theme.spacing.xxl,
    },
    iconCircle: {
      width: 72,
      height: 72,
      borderRadius: 36,
      alignItems: 'center',
      justifyContent: 'center',
      marginBottom: theme.spacing.xs,
      backgroundColor: theme.colors.brand.primaryContainer,
    },
    action: {
      marginTop: theme.spacing.sm,
    },
  });
