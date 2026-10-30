import { useEffect, useState, type ReactNode } from 'react';
import { Animated, Modal, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { OPIconButton } from '../atoms/OPIconButton';
import { OPText } from '../atoms/OPText';
import { useReducedMotion } from '../hooks/useReducedMotion';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface ModalSheetProps {
  visible: boolean;
  onClose: () => void;
  title: string;
  /** Spoken name of the close button. */
  closeLabel: string;
  children: ReactNode;
}

/** Bottom sheet dialog. Closes with the close button, the backdrop or the Android back button. */
export function ModalSheet({ visible, onClose, title, closeLabel, children }: ModalSheetProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const insets = useSafeAreaInsets();
  const reducedMotion = useReducedMotion();
  const [offset] = useState(() => new Animated.Value(0));

  useEffect(() => {
    if (!visible) return;
    if (reducedMotion) {
      offset.setValue(0);
      return;
    }
    offset.setValue(48);
    Animated.timing(offset, {
      toValue: 0,
      duration: theme.motion.duration.standard,
      useNativeDriver: true,
    }).start();
  }, [visible, reducedMotion, offset, theme.motion.duration.standard]);

  return (
    <Modal
      visible={visible}
      transparent
      animationType={reducedMotion ? 'none' : 'fade'}
      statusBarTranslucent
      navigationBarTranslucent
      onRequestClose={onClose}
    >
      <View style={styles.root}>
        <Pressable
          style={[StyleSheet.absoluteFill, { backgroundColor: theme.colors.scrim }]}
          onPress={onClose}
          accessible={false}
          importantForAccessibility="no"
          testID="modal-sheet-backdrop"
        />
        <Animated.View
          accessibilityViewIsModal
          style={[
            styles.sheet,
            {
              paddingBottom: Math.max(insets.bottom, theme.spacing.md) + theme.spacing.xs,
              transform: [{ translateY: offset }],
            },
            theme.elevation.raised ? { boxShadow: theme.elevation.raised } : null,
          ]}
        >
          <View style={styles.handle} />
          <View style={styles.header}>
            <OPText variant="heading" accessibilityRole="header" style={styles.title}>
              {title}
            </OPText>
            <OPIconButton icon="x" accessibilityLabel={closeLabel} onPress={onClose} />
          </View>
          {children}
        </Animated.View>
      </View>
    </Modal>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    root: {
      flex: 1,
      justifyContent: 'flex-end',
    },
    sheet: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
      paddingTop: theme.spacing.xs,
      borderTopLeftRadius: theme.radii.xl,
      borderTopRightRadius: theme.radii.xl,
      backgroundColor: theme.colors.surface,
    },
    handle: {
      alignSelf: 'center',
      width: 40,
      height: 4,
      borderRadius: 2,
      backgroundColor: theme.colors.border,
    },
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
    },
    title: {
      flex: 1,
    },
  });
