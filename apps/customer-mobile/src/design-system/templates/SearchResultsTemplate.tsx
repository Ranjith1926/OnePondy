import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface SearchResultsTemplateProps {
  /** Header row, typically a back button and a search field. */
  header: ReactNode;
  children: ReactNode;
}

export function SearchResultsTemplate({ header, children }: SearchResultsTemplateProps) {
  const styles = useThemedStyles(createStyles);
  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      <View style={styles.header}>{header}</View>
      <View style={styles.content}>{children}</View>
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
      gap: theme.spacing.xxs,
      paddingLeft: theme.spacing.xxs,
      paddingRight: theme.layout.screenPadding,
      paddingVertical: theme.spacing.xs,
    },
    content: {
      flex: 1,
    },
  });
