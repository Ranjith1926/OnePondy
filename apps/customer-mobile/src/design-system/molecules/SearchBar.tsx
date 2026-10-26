import { Pressable, StyleSheet } from 'react-native';

import { OPIcon } from '../atoms/OPIcon';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface SearchBarProps {
  placeholder: string;
  onPress: () => void;
  accessibilityHint?: string;
}

/** Search entry point that opens a dedicated search screen. */
export function SearchBar({ placeholder, onPress, accessibilityHint }: SearchBarProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={placeholder}
      accessibilityHint={accessibilityHint}
      onPress={onPress}
      style={({ pressed }) => [
        styles.bar,
        theme.elevation.card ? { boxShadow: theme.elevation.card } : null,
        pressed ? styles.pressed : null,
      ]}
    >
      <OPIcon name="search" size={20} color={theme.colors.brand.primary} />
      <OPText tone="secondary" numberOfLines={2} style={styles.placeholder}>
        {placeholder}
      </OPText>
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    bar: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
      minHeight: 52,
      paddingHorizontal: theme.spacing.md,
      borderRadius: theme.radii.pill,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    pressed: {
      backgroundColor: theme.colors.surfaceMuted,
    },
    placeholder: {
      flexShrink: 1,
    },
  });
