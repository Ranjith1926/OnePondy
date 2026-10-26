import { Pressable, StyleSheet, View } from 'react-native';

import { OPIcon } from '../atoms/OPIcon';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface LocationHeaderProps {
  /** Small label above the area, e.g. "Your area". */
  caption: string;
  areaName: string;
  onPress: () => void;
  accessibilityHint?: string;
}

export function LocationHeader({
  caption,
  areaName,
  onPress,
  accessibilityHint,
}: LocationHeaderProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${caption}: ${areaName}`}
      accessibilityHint={accessibilityHint}
      onPress={onPress}
      style={({ pressed }) => [styles.container, pressed ? styles.pressed : null]}
    >
      <OPIcon name="map-pin" size={22} color={theme.colors.brand.primary} />
      <View style={styles.text}>
        <OPText variant="caption" tone="secondary">
          {caption}
        </OPText>
        <View style={styles.areaRow}>
          <OPText variant="subheading" numberOfLines={1} style={styles.area}>
            {areaName}
          </OPText>
          <OPIcon name="chevron-down" size={18} color={theme.colors.text.secondary} />
        </View>
      </View>
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    container: {
      flexShrink: 1,
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xs,
      minHeight: theme.layout.minTouchTarget,
      paddingVertical: theme.spacing.xxs,
      paddingRight: theme.spacing.xs,
      borderRadius: theme.radii.md,
    },
    pressed: {
      opacity: theme.motion.pressedOpacity,
    },
    text: {
      flexShrink: 1,
    },
    areaRow: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xxs,
    },
    area: {
      flexShrink: 1,
    },
  });
