import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { OPIconButton } from '../atoms/OPIconButton';
import { OPText } from '../atoms/OPText';
import { useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface ModalScreenTemplateProps {
  title: string;
  /** Spoken name of the close button. */
  closeLabel: string;
  onClose: () => void;
  children: ReactNode;
}

/** Full-screen modal layout with a title and a close button. */
export function ModalScreenTemplate({
  title,
  closeLabel,
  onClose,
  children,
}: ModalScreenTemplateProps) {
  const styles = useThemedStyles(createStyles);
  return (
    <SafeAreaView edges={['top', 'bottom', 'left', 'right']} style={styles.screen}>
      <View style={styles.header}>
        <OPText variant="title" accessibilityRole="header" style={styles.title}>
          {title}
        </OPText>
        <OPIconButton icon="x" accessibilityLabel={closeLabel} onPress={onClose} variant="tonal" />
      </View>
      <ScrollView contentContainerStyle={styles.content}>{children}</ScrollView>
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
      paddingLeft: theme.layout.screenPadding,
      paddingRight: theme.spacing.sm,
      paddingVertical: theme.spacing.xs,
    },
    title: {
      flex: 1,
    },
    content: {
      gap: theme.spacing.md,
      paddingBottom: theme.spacing.xxl,
    },
  });
