import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  ListItem,
  ListSection,
  ModalScreenTemplate,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { setSelectedLocation, useAppDispatch } from '../../../state';
import { useSelectedArea } from '../hooks/useSelectedArea';
import { serviceAreaIds } from '../serviceAreas';

export function LocationSelectorScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const dispatch = useAppDispatch();
  const styles = useThemedStyles(createStyles);
  const selected = useSelectedArea();

  return (
    <ModalScreenTemplate
      title={t('location.title')}
      closeLabel={t('common.close')}
      onClose={() => navigation.goBack()}
    >
      <OPText tone="secondary" style={styles.description}>
        {t('location.description')}
      </OPText>
      <View accessibilityRole="radiogroup">
        <ListSection>
          {serviceAreaIds.map((areaId) => (
            <ListItem
              key={areaId}
              title={t(`areas.${areaId}`)}
              subtitle={areaId === 'puducherry' ? t('location.wholeCity') : undefined}
              trailing={{ kind: 'radio', selected: selected.areaId === areaId }}
              onPress={() => {
                dispatch(setSelectedLocation(areaId));
                navigation.goBack();
              }}
            />
          ))}
        </ListSection>
      </View>
    </ModalScreenTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    description: {
      paddingHorizontal: theme.layout.screenPadding,
    },
  });
