import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { InlineBanner, useTheme } from '../../design-system';
import { hideBanner, useAppDispatch, useAppState } from '../../state';

/** Shows the global banner from app state above the current screen. */
export function BannerHost() {
  const { t } = useTranslation();
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const dispatch = useAppDispatch();
  const { ui } = useAppState();
  if (!ui.banner) {
    return null;
  }

  return (
    <View
      pointerEvents="box-none"
      style={[
        styles.host,
        { top: insets.top + theme.spacing.xs, paddingHorizontal: theme.layout.screenPadding },
      ]}
    >
      <InlineBanner
        kind={ui.banner.kind}
        message={t(ui.banner.messageKey)}
        dismissLabel={t('common.dismiss')}
        onDismiss={() => dispatch(hideBanner())}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  host: {
    position: 'absolute',
    left: 0,
    right: 0,
  },
});
