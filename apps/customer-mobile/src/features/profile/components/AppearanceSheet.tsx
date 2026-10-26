import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { ListItem, ModalSheet, useThemedStyles, type Theme } from '../../../design-system';
import { setTheme, useAppDispatch, useAppState, type ThemePreference } from '../../../state';

const options: readonly ThemePreference[] = ['system', 'light', 'dark'];

interface AppearanceSheetProps {
  visible: boolean;
  onClose: () => void;
}

export function AppearanceSheet({ visible, onClose }: AppearanceSheetProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const dispatch = useAppDispatch();
  const { preferences } = useAppState();

  return (
    <ModalSheet
      visible={visible}
      onClose={onClose}
      title={t('appearance.title')}
      closeLabel={t('common.close')}
    >
      <View accessibilityRole="radiogroup" style={styles.list}>
        {options.map((option) => (
          <ListItem
            key={option}
            title={t(`appearance.${option}`)}
            trailing={{ kind: 'radio', selected: preferences.theme === option }}
            onPress={() => {
              dispatch(setTheme(option));
              onClose();
            }}
          />
        ))}
      </View>
    </ModalSheet>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    // Align row content with the sheet title.
    list: {
      marginHorizontal: -theme.spacing.md,
    },
  });
