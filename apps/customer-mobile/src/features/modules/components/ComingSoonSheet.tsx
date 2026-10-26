import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  ModalSheet,
  OPButton,
  OPIcon,
  OPText,
  useTheme,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import type { ModuleKey } from '../moduleConfig';

interface ComingSoonSheetProps {
  /** The module to explain, or null when the sheet is closed. */
  module: ModuleKey | null;
  onClose: () => void;
}

export function ComingSoonSheet({ module, onClose }: ComingSoonSheetProps) {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  // Keep showing the last module while the sheet fades out.
  const [shown, setShown] = useState<ModuleKey | null>(module);
  if (module !== null && module !== shown) {
    setShown(module);
  }

  return (
    <ModalSheet
      visible={module !== null}
      onClose={onClose}
      title={shown ? t('comingSoon.title', { module: t(`modules.${shown}`) }) : ''}
      closeLabel={t('common.close')}
    >
      {shown ? (
        <View style={styles.body}>
          <OPText tone="secondary">{t(`comingSoon.messages.${shown}`)}</OPText>
          <View style={styles.promise}>
            <OPIcon name="shield-check" size={20} color={theme.colors.success} />
            <OPText variant="label" style={styles.promiseText}>
              {t('comingSoon.promise')}
            </OPText>
          </View>
          <OPButton label={t('common.gotIt')} onPress={onClose} fullWidth />
        </View>
      ) : null}
    </ModalSheet>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    body: {
      gap: theme.spacing.md,
    },
    promise: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xs,
      padding: theme.spacing.sm,
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.surfaceMuted,
    },
    promiseText: {
      flex: 1,
    },
  });
