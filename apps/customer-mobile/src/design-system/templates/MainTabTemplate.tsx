import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { OPText } from '../atoms/OPText';
import { useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface MainTabTemplateProps {
  /** Large screen title. Use `header` instead for a custom header row. */
  title?: string;
  header?: ReactNode;
  headerRight?: ReactNode;
  children: ReactNode;
}

/** Layout for bottom-tab screens: top safe area, header and scrolling content. */
export function MainTabTemplate({ title, header, headerRight, children }: MainTabTemplateProps) {
  const styles = useThemedStyles(createStyles);
  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
      >
        {header ?? null}
        {title ? (
          <View style={styles.titleRow}>
            <OPText variant="title" accessibilityRole="header" style={styles.title}>
              {title}
            </OPText>
            {headerRight}
          </View>
        ) : null}
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
    content: {
      gap: theme.spacing.xl,
      paddingTop: theme.spacing.xs,
      paddingBottom: theme.spacing.xxl,
    },
    titleRow: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
      minHeight: theme.layout.minTouchTarget,
      paddingHorizontal: theme.layout.screenPadding,
    },
    title: {
      flex: 1,
    },
  });
