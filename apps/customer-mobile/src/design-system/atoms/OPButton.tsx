import { ActivityIndicator, Pressable, StyleSheet } from 'react-native';

import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';
import { OPIcon, type IconName } from './OPIcon';
import { OPText } from './OPText';

export type ButtonVariant = 'primary' | 'secondary' | 'tertiary';
export type ButtonSize = 'sm' | 'md' | 'lg';

export interface OPButtonProps {
  label: string;
  onPress: () => void;
  variant?: ButtonVariant;
  size?: ButtonSize;
  icon?: IconName;
  iconPosition?: 'start' | 'end';
  loading?: boolean;
  disabled?: boolean;
  fullWidth?: boolean;
  accessibilityHint?: string;
  testID?: string;
}

const minHeights: Record<ButtonSize, number> = { sm: 40, md: 48, lg: 56 };

function colorsFor(theme: Theme, variant: ButtonVariant, disabled: boolean) {
  const { brand, text, surfaceMuted } = theme.colors;
  if (disabled) {
    return {
      background: variant === 'tertiary' ? 'transparent' : surfaceMuted,
      pressedBackground: undefined,
      foreground: text.disabled,
    };
  }
  switch (variant) {
    case 'primary':
      return {
        background: brand.primary,
        pressedBackground: brand.primaryPressed,
        foreground: brand.onPrimary,
      };
    case 'secondary':
      return {
        background: brand.primaryContainer,
        pressedBackground: undefined,
        foreground: brand.onPrimaryContainer,
      };
    case 'tertiary':
      return { background: 'transparent', pressedBackground: surfaceMuted, foreground: text.link };
  }
}

export function OPButton({
  label,
  onPress,
  variant = 'primary',
  size = 'md',
  icon,
  iconPosition = 'start',
  loading = false,
  disabled = false,
  fullWidth = false,
  accessibilityHint,
  testID,
}: OPButtonProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const inactive = disabled || loading;
  const { background, pressedBackground, foreground } = colorsFor(theme, variant, disabled);
  const iconSize = size === 'sm' ? 18 : 20;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled: inactive, busy: loading }}
      disabled={inactive}
      onPress={onPress}
      hitSlop={size === 'sm' ? 4 : undefined}
      testID={testID}
      style={({ pressed }) => [
        styles.base,
        size === 'sm' ? styles.small : null,
        { minHeight: minHeights[size], backgroundColor: background },
        pressed && pressedBackground ? { backgroundColor: pressedBackground } : null,
        pressed && !pressedBackground ? { opacity: theme.motion.pressedOpacity } : null,
        fullWidth ? styles.fullWidth : null,
      ]}
    >
      {loading ? <ActivityIndicator size="small" color={foreground} /> : null}
      {!loading && icon && iconPosition === 'start' ? (
        <OPIcon name={icon} size={iconSize} color={foreground} />
      ) : null}
      <OPText
        variant={size === 'sm' ? 'label' : 'bodyStrong'}
        style={{ color: foreground }}
        numberOfLines={2}
        align="center"
      >
        {label}
      </OPText>
      {!loading && icon && iconPosition === 'end' ? (
        <OPIcon name={icon} size={iconSize} color={foreground} />
      ) : null}
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    base: {
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'center',
      gap: theme.spacing.xs,
      paddingHorizontal: theme.spacing.lg,
      paddingVertical: theme.spacing.xs,
      borderRadius: theme.radii.pill,
    },
    small: {
      paddingHorizontal: theme.spacing.md,
    },
    fullWidth: {
      alignSelf: 'stretch',
    },
  });
