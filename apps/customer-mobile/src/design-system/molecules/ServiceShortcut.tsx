import { Pressable, StyleSheet, View } from 'react-native';

import { OPBadge } from '../atoms/OPBadge';
import { OPIcon, type IconName } from '../atoms/OPIcon';
import { OPText } from '../atoms/OPText';
import { useLargeText } from '../hooks/useLargeText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface ServiceShortcutProps {
  label: string;
  icon: IconName;
  onPress: () => void;
  /** Status shown on the tile, e.g. "Soon". Included in the spoken label. */
  badgeLabel?: string;
}

export function ServiceShortcut({ label, icon, onPress, badgeLabel }: ServiceShortcutProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  // With large system text the badge no longer fits over the tile, so it moves under the label.
  const largeText = useLargeText();
  const badge = badgeLabel ? <OPBadge label={badgeLabel} tone="accent" /> : null;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={badgeLabel ? `${label}, ${badgeLabel}` : label}
      onPress={onPress}
      style={({ pressed }) => [styles.container, pressed ? styles.pressed : null]}
    >
      <View style={styles.tile}>
        <OPIcon name={icon} size={26} color={theme.colors.brand.primary} />
      </View>
      <OPText variant="label" align="center">
        {label}
      </OPText>
      {/* The wrapper sizes to the badge so it centres under the label. */}
      {badge && largeText ? <View>{badge}</View> : null}
      {badge && !largeText ? <View style={styles.overlayBadge}>{badge}</View> : null}
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    container: {
      flex: 1,
      alignItems: 'center',
      gap: theme.spacing.xs,
      minHeight: theme.layout.minTouchTarget,
      paddingTop: theme.spacing.xs,
    },
    pressed: {
      opacity: theme.motion.pressedOpacity,
    },
    tile: {
      width: 60,
      height: 60,
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radii.lg,
      backgroundColor: theme.colors.brand.primaryContainer,
    },
    overlayBadge: {
      position: 'absolute',
      top: 0,
      right: 0,
    },
  });
