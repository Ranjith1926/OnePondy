import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { ListItem, ModalSheet, useThemedStyles, type Theme } from '../../../design-system';
import { languageOptions } from '../../../i18n';
import { setLanguage, useAppDispatch, useAppState } from '../../../state';

interface LanguageSheetProps {
  visible: boolean;
  onClose: () => void;
}

export function LanguageSheet({ visible, onClose }: LanguageSheetProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const dispatch = useAppDispatch();
  const { preferences } = useAppState();

  return (
    <ModalSheet
      visible={visible}
      onClose={onClose}
      title={t('language.title')}
      closeLabel={t('common.close')}
    >
      <View accessibilityRole="radiogroup" style={styles.list}>
        {languageOptions.map((option) => (
          <ListItem
            key={option.code}
            title={option.nativeName}
            subtitle={option.available ? undefined : t('language.translationPending')}
            disabled={!option.available}
            trailing={{ kind: 'radio', selected: preferences.language === option.code }}
            onPress={() => {
              dispatch(setLanguage(option.code));
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
    list: {
      marginHorizontal: -theme.spacing.md,
    },
  });
