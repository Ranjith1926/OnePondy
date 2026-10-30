import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { OPIconButton, OPText, useThemedStyles, type Theme } from '../design-system';

interface ScreenProps {
  title: string;
  /** Shows a back button with this spoken name when given with `onBack`. */
  backLabel?: string;
  onBack?: () => void;
  children: ReactNode;
}

/** Full-screen layout with a title, an optional back button and scrolling content that stays above the system bars. */
export function Screen({ title, backLabel, onBack, children }: ScreenProps) {
  const styles = useThemedStyles(createStyles);
  return (
    <SafeAreaView edges={['top', 'bottom', 'left', 'right']} style={styles.screen}>
      <View style={styles.header}>
        {onBack && backLabel ? (
          <OPIconButton
            icon="arrow-left"
            accessibilityLabel={backLabel}
            onPress={onBack}
            variant="tonal"
          />
        ) : null}
        <OPText variant="title" accessibilityRole="header" style={styles.title}>
          {title}
        </OPText>
      </View>
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        {children}
      </ScrollView>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
      paddingVertical: theme.spacing.xs,
    },
    title: {
      flex: 1,
    },
    content: {
      gap: theme.spacing.md,
      paddingHorizontal: theme.layout.screenPadding,
      paddingBottom: theme.spacing.xxl,
    },
  });
