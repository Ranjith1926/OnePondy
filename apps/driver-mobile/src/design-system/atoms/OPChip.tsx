import { Pressable, StyleSheet } from 'react-native';

import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';
import { OPIcon, type IconName } from './OPIcon';
import { OPText } from './OPText';

interface OPChipProps {
  label: string;
  onPress: () => void;
  selected?: boolean;
  icon?: IconName;
  testID?: string;
}

export function OPChip({ label, onPress, selected = false, icon, testID }: OPChipProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const foreground = selected ? theme.colors.brand.onPrimary : theme.colors.text.primary;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ selected }}
      onPress={onPress}
      hitSlop={{ top: 4, bottom: 4 }}
      testID={testID}
      style={({ pressed }) => [
        styles.chip,
        selected ? styles.selected : null,
        pressed ? { opacity: theme.motion.pressedOpacity } : null,
      ]}
    >
      {icon ? <OPIcon name={icon} size={16} color={foreground} /> : null}
      <OPText variant="label" style={{ color: foreground }} numberOfLines={1}>
        {label}
      </OPText>
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    chip: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xxs,
      minHeight: 40,
      paddingHorizontal: theme.spacing.md,
      borderRadius: theme.radii.pill,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    selected: {
      borderColor: theme.colors.brand.primary,
      backgroundColor: theme.colors.brand.primary,
    },
  });
