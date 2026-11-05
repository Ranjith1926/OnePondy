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
  /** A toggle that is switched on (a saved heart): drawn in the brand colour on a tinted circle, and announced as selected. */
  active?: boolean;
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
  active = false,
  disabled = false,
  accessibilityHint,
  testID,
}: OPIconButtonProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const box = size === 'md' ? theme.layout.minTouchTarget : 40;
  const iconColor = disabled
    ? theme.colors.text.disabled
    : active
      ? theme.colors.brand.onPrimaryContainer
      : theme.colors.text.primary;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled, selected: active }}
      disabled={disabled}
      onPress={onPress}
      hitSlop={size === 'sm' ? 4 : undefined}
      testID={testID}
      style={({ pressed }) => [
        styles.base,
        { width: box, height: box },
        variant === 'tonal' ? styles.tonal : null,
        active ? styles.active : null,
        pressed ? styles.pressed : null,
      ]}
    >
      <OPIcon name={icon} size={size === 'md' ? 24 : 20} color={iconColor} />
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
    active: {
      backgroundColor: theme.colors.brand.primaryContainer,
    },
    pressed: {
      backgroundColor: theme.colors.border,
    },
  });
