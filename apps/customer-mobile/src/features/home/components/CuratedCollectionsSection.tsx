import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  OPBadge,
  OPIcon,
  OPText,
  SectionWithHeader,
  useTheme,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { sampleCollections, type CollectionSummary } from '../data/sampleCollections';

function CollectionCard({ collection }: { collection: CollectionSummary }) {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const sampleLabel = collection.isSample ? t('common.sample') : undefined;

  return (
    <View
      accessible
      accessibilityLabel={[collection.title, collection.description, sampleLabel]
        .filter(Boolean)
        .join(', ')}
      style={styles.card}
    >
      <View style={styles.iconTile}>
        <OPIcon name={collection.icon} size={24} color={theme.colors.brand.onSecondaryContainer} />
      </View>
      <View style={styles.text}>
        <OPText variant="subheading">{collection.title}</OPText>
        <OPText variant="caption" tone="secondary">
          {collection.description}
        </OPText>
        {sampleLabel ? <OPBadge label={sampleLabel} /> : null}
      </View>
    </View>
  );
}

export function CuratedCollectionsSection() {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  if (sampleCollections.length === 0) {
    return null;
  }

  return (
    <SectionWithHeader title={t('home.collections.title')}>
      <View style={styles.list}>
        {sampleCollections.map((collection) => (
          <CollectionCard key={collection.id} collection={collection} />
        ))}
      </View>
    </SectionWithHeader>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    list: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
    },
    card: {
      flexDirection: 'row',
      alignItems: 'flex-start',
      gap: theme.spacing.sm,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    iconTile: {
      width: 48,
      height: 48,
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.brand.secondaryContainer,
    },
    text: {
      flex: 1,
      gap: theme.spacing.xxs,
    },
  });
