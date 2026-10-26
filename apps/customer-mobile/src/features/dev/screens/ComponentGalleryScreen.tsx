// Development-only design QA screen. It is registered only in __DEV__ builds and is not
// user-facing, so its sample copy is not translated (excluded from the i18n lint rule).
import { useNavigation } from '@react-navigation/native';
import { useState, type ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  EmptyState,
  ErrorState,
  FormField,
  InlineBanner,
  ListItem,
  ListSection,
  LoadingState,
  ModalSheet,
  OPAvatar,
  OPBadge,
  OPButton,
  OPChip,
  OPIconButton,
  OPSearchField,
  OPSkeleton,
  OPSpinner,
  OPText,
  PlaceCard,
  ServiceShortcut,
  typography,
  useTheme,
  useThemedStyles,
  type BadgeTone,
  type TextVariant,
  type Theme,
} from '../../../design-system';

const badgeTones: BadgeTone[] = [
  'neutral',
  'brand',
  'accent',
  'success',
  'warning',
  'error',
  'info',
];

function GallerySection({ title, children }: { title: string; children: ReactNode }) {
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.section}>
      <OPText variant="heading" accessibilityRole="header">
        {title}
      </OPText>
      {children}
    </View>
  );
}

export function ComponentGalleryScreen() {
  const navigation = useNavigation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const [search, setSearch] = useState('');
  const [chip, setChip] = useState('heritage');
  const [sheetOpen, setSheetOpen] = useState(false);
  const [retries, setRetries] = useState(0);
  const [radio, setRadio] = useState('b');

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      <View style={styles.header}>
        <OPIconButton
          icon="arrow-left"
          accessibilityLabel="Back"
          onPress={() => navigation.goBack()}
        />
        <OPText variant="title">Component gallery</OPText>
      </View>
      <ScrollView contentContainerStyle={styles.content}>
        <GallerySection title="Typography">
          {(Object.keys(typography) as TextVariant[]).map((variant) => (
            <OPText key={variant} variant={variant}>
              {variant} — Discover Puducherry
            </OPText>
          ))}
        </GallerySection>

        <GallerySection title="Colours">
          <View style={styles.swatches}>
            {[
              ['primary', theme.colors.brand.primary],
              ['secondary', theme.colors.brand.secondary],
              ['background', theme.colors.background],
              ['surface', theme.colors.surface],
              ['success', theme.colors.success],
              ['warning', theme.colors.warning],
              ['error', theme.colors.error],
              ['info', theme.colors.info],
            ].map(([name, color]) => (
              <View key={name} style={styles.swatch}>
                <View style={[styles.swatchColor, { backgroundColor: color }]} />
                <OPText variant="caption">{name}</OPText>
              </View>
            ))}
          </View>
        </GallerySection>

        <GallerySection title="Buttons">
          <View style={styles.buttons}>
            <OPButton label="Primary" onPress={() => {}} />
            <OPButton
              label="Secondary with icon"
              onPress={() => {}}
              variant="secondary"
              icon="map"
            />
            <OPButton label="Tertiary" onPress={() => {}} variant="tertiary" />
            <OPButton label="Saving" onPress={() => {}} loading />
            <OPButton label="Disabled" onPress={() => {}} disabled />
            <OPButton label="Small" onPress={() => {}} size="sm" />
          </View>
          <OPButton label="Large, full width" onPress={() => {}} size="lg" fullWidth />
          <View style={styles.row}>
            <OPIconButton icon="bell" accessibilityLabel="Plain icon button" onPress={() => {}} />
            <OPIconButton
              icon="heart"
              accessibilityLabel="Tonal icon button"
              onPress={() => {}}
              variant="tonal"
            />
            <OPIconButton icon="x" accessibilityLabel="Disabled" onPress={() => {}} disabled />
          </View>
        </GallerySection>

        <GallerySection title="Inputs">
          <OPSearchField
            value={search}
            onChangeText={setSearch}
            placeholder="Search places"
            clearLabel="Clear search"
          />
          <FormField label="Full name" placeholder="As on your ID" hint="Used for bookings" />
          <FormField label="Email" defaultValue="name@" error="Enter a complete email address" />
          <FormField label="Read only" defaultValue="Puducherry" editable={false} />
        </GallerySection>

        <GallerySection title="Chips and badges">
          <View style={styles.wrapRow}>
            {['heritage', 'beach', 'nature'].map((value) => (
              <OPChip
                key={value}
                label={value}
                selected={chip === value}
                onPress={() => setChip(value)}
              />
            ))}
          </View>
          <View style={styles.wrapRow}>
            {badgeTones.map((tone) => (
              <OPBadge key={tone} label={tone} tone={tone} />
            ))}
          </View>
          <View style={styles.row}>
            <OPAvatar />
            <OPAvatar name="Asha Raman" />
            <OPSpinner accessibilityLabel="Loading" />
          </View>
        </GallerySection>

        <GallerySection title="Banners">
          <InlineBanner kind="info" message="Information banner" />
          <InlineBanner kind="success" message="Saved" />
          <InlineBanner kind="warning" message="Limited availability information" />
          <InlineBanner
            kind="error"
            message="Couldn't save"
            onDismiss={() => {}}
            dismissLabel="Dismiss"
          />
        </GallerySection>

        <GallerySection title="Cards and rows">
          <View style={styles.row}>
            <ServiceShortcut label="Food" icon="utensils" badgeLabel="Soon" onPress={() => {}} />
            <ServiceShortcut label="Rides" icon="car-taxi-front" onPress={() => {}} />
            <ServiceShortcut label="Stays" icon="bed-double" onPress={() => {}} />
            <ServiceShortcut label="Activities" icon="ticket" onPress={() => {}} />
          </View>
          <PlaceCard
            title="Promenade Beach"
            subtitle="Beach · White Town"
            fallbackIcon="waves"
            badgeLabel="Sample"
          />
          <PlaceCard
            title="Pondicherry Museum"
            subtitle="Museum · White Town"
            fallbackIcon="landmark"
            variant="list"
            onPress={() => {}}
          />
          <View style={styles.bleed}>
            <ListSection title="List section">
              <ListItem
                title="Chevron row"
                leadingIcon="map"
                trailing={{ kind: 'chevron' }}
                onPress={() => {}}
              />
              <ListItem
                title="Value row"
                leadingIcon="languages"
                trailing={{ kind: 'value', text: 'English' }}
                onPress={() => {}}
              />
              <ListItem
                title="Option A"
                trailing={{ kind: 'radio', selected: radio === 'a' }}
                onPress={() => setRadio('a')}
              />
              <ListItem
                title="Option B"
                trailing={{ kind: 'radio', selected: radio === 'b' }}
                onPress={() => setRadio('b')}
              />
              <ListItem
                title="Disabled option"
                subtitle="Not available yet"
                disabled
                trailing={{ kind: 'radio', selected: false }}
                onPress={() => {}}
              />
            </ListSection>
          </View>
        </GallerySection>

        <GallerySection title="Loading, empty and error states">
          <OPSkeleton height={20} width="60%" />
          <LoadingState accessibilityLabel="Loading places" rows={2} />
          <EmptyState
            icon="search-x"
            title="No places here yet"
            message="Places in this category will appear once they are verified."
            actionLabel="Show all places"
            onAction={() => {}}
          />
          <ErrorState
            title="Something went wrong"
            message={`Retried ${retries} times.`}
            retryLabel="Try again"
            onRetry={() => setRetries(retries + 1)}
          />
          <ErrorState
            kind="offline"
            title="You're offline"
            message="Reconnect to see the latest information."
            retryLabel="Try again"
            onRetry={() => {}}
          />
        </GallerySection>

        <GallerySection title="Modal sheet">
          <OPButton label="Open sheet" onPress={() => setSheetOpen(true)} variant="secondary" />
        </GallerySection>
      </ScrollView>
      <ModalSheet
        visible={sheetOpen}
        onClose={() => setSheetOpen(false)}
        title="Sheet title"
        closeLabel="Close"
      >
        <OPText tone="secondary">
          Sheets close with the button, the backdrop or the back button.
        </OPText>
        <OPButton label="Done" onPress={() => setSheetOpen(false)} fullWidth />
      </ModalSheet>
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
      gap: theme.spacing.xs,
      paddingHorizontal: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
    },
    content: {
      gap: theme.spacing.xxl,
      padding: theme.layout.screenPadding,
      paddingBottom: theme.spacing.xxl * 2,
    },
    section: {
      gap: theme.spacing.sm,
    },
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
    },
    buttons: {
      alignItems: 'flex-start',
      gap: theme.spacing.sm,
    },
    wrapRow: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.xs,
    },
    swatches: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.sm,
    },
    swatch: {
      alignItems: 'center',
      gap: theme.spacing.xxs,
      width: 88,
    },
    swatchColor: {
      width: 48,
      height: 48,
      borderRadius: theme.radii.md,
      borderWidth: 1,
      borderColor: theme.colors.border,
    },
    bleed: {
      marginHorizontal: -theme.layout.screenPadding,
    },
  });
