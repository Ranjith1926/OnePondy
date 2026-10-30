import { Pressable, StyleSheet } from 'react-native';

import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';
import { OPIcon, type IconName } from './OPIcon';

interface OPIconButtonProps {
  icon: IconName;
  /** Required: icon-only buttons need a spoken name. */
  accessibilityLabel: string;
  onPress: () => void;
  variant?: 'plain' | 'tonal';
  size?: 'sm' | 'md';
  disabled?: boolean;
  accessibilityHint?: string;
  testID?: string;
}

export function OPIconButton({
  icon,
  accessibilityLabel,
  onPress,
  variant = 'plain',
  size = 'md',
  disabled = false,
  accessibilityHint,
  testID,
}: OPIconButtonProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const box = size === 'md' ? theme.layout.minTouchTarget : 40;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      hitSlop={size === 'sm' ? 4 : undefined}
      testID={testID}
      style={({ pressed }) => [
        styles.base,
        { width: box, height: box },
        variant === 'tonal' ? styles.tonal : null,
        pressed ? styles.pressed : null,
      ]}
    >
      <OPIcon
        name={icon}
        size={size === 'md' ? 24 : 20}
        color={disabled ? theme.colors.text.disabled : theme.colors.text.primary}
      />
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    base: {
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radii.pill,
    },
    tonal: {
      backgroundColor: theme.colors.surfaceMuted,
    },
    pressed: {
      backgroundColor: theme.colors.border,
    },
  });
