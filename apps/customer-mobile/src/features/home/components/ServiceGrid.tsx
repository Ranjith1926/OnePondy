import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  ServiceShortcut,
  useLargeText,
  useThemedStyles,
  type IconName,
  type Theme,
} from '../../../design-system';
import { moduleStatus, type ModuleKey } from '../../modules/moduleConfig';

const services: { module: ModuleKey; icon: IconName }[] = [
  { module: 'food', icon: 'utensils' },
  { module: 'rides', icon: 'car-taxi-front' },
  { module: 'stays', icon: 'bed-double' },
  { module: 'activities', icon: 'ticket' },
];

interface ServiceGridProps {
  onServicePress: (module: ModuleKey) => void;
}

export function ServiceGrid({ onServicePress }: ServiceGridProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  // One row of four; two rows of two when the system font is large.
  const perRow = useLargeText() ? 2 : 4;
  const visible = services.filter(({ module }) => moduleStatus[module] !== 'hidden');
  if (visible.length === 0) {
    return null;
  }

  const rows: (typeof visible)[] = [];
  for (let index = 0; index < visible.length; index += perRow) {
    rows.push(visible.slice(index, index + perRow));
  }

  return (
    <View style={styles.grid} accessibilityLabel={t('home.servicesLabel')}>
      {rows.map((row) => (
        <View key={row.map((item) => item.module).join('-')} style={styles.row}>
          {row.map(({ module, icon }) => (
            <ServiceShortcut
              key={module}
              label={t(`modules.${module}`)}
              icon={icon}
              badgeLabel={moduleStatus[module] === 'comingSoon' ? t('common.soon') : undefined}
              onPress={() => onServicePress(module)}
            />
          ))}
        </View>
      ))}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    grid: {
      gap: theme.spacing.md,
      paddingHorizontal: theme.layout.screenPadding,
    },
    row: {
      flexDirection: 'row',
      gap: theme.spacing.xs,
    },
  });
