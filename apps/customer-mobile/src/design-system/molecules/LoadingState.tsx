import { StyleSheet, View } from 'react-native';

import { OPSkeleton } from '../atoms/OPSkeleton';
import { useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface LoadingStateProps {
  /** Spoken while content loads, e.g. "Loading places". */
  accessibilityLabel: string;
  rows?: number;
}

/** Skeleton list announced to screen readers as a single busy element. */
export function LoadingState({ accessibilityLabel, rows = 3 }: LoadingStateProps) {
  const styles = useThemedStyles(createStyles);
  return (
    <View
      accessible
      accessibilityRole="progressbar"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ busy: true }}
      accessibilityLiveRegion="polite"
      style={styles.container}
    >
      {Array.from({ length: rows }, (_, index) => (
        <View key={index} style={styles.row}>
          <OPSkeleton width={88} height={88} radius="md" />
          <View style={styles.lines}>
            <OPSkeleton height={18} width="80%" radius="sm" />
            <OPSkeleton height={14} width="55%" radius="sm" />
          </View>
        </View>
      ))}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    container: {
      gap: theme.spacing.md,
    },
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
    },
    lines: {
      flex: 1,
      gap: theme.spacing.xs,
    },
  });
