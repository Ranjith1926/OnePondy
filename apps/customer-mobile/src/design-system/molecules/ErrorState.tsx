import { StyleSheet, View } from 'react-native';

import { OPButton } from '../atoms/OPButton';
import { OPIcon } from '../atoms/OPIcon';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface ErrorStateProps {
  title: string;
  message: string;
  retryLabel: string;
  onRetry: () => void;
  /** `offline` shows a connectivity icon instead of a warning. */
  kind?: 'error' | 'offline';
  retrying?: boolean;
}

export function ErrorState({
  title,
  message,
  retryLabel,
  onRetry,
  kind = 'error',
  retrying = false,
}: ErrorStateProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.container} accessibilityLiveRegion="polite">
      <View style={styles.iconCircle}>
        <OPIcon
          name={kind === 'offline' ? 'wifi-off' : 'triangle-alert'}
          size={30}
          color={theme.colors.onErrorContainer}
        />
      </View>
      <OPText variant="heading" align="center" accessibilityRole="header">
        {title}
      </OPText>
      <OPText tone="secondary" align="center">
        {message}
      </OPText>
      <View style={styles.action}>
        <OPButton label={retryLabel} onPress={onRetry} icon="refresh-cw" loading={retrying} />
      </View>
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
      backgroundColor: theme.colors.errorContainer,
    },
    action: {
      marginTop: theme.spacing.sm,
    },
  });
