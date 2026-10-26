import type { ReactElement } from 'react';
import { FlatList } from 'react-native';

import { useTheme } from '../theme/ThemeProvider';

interface HorizontalCarouselProps<T> {
  data: readonly T[];
  renderItem: (item: T) => ReactElement;
  keyExtractor: (item: T) => string;
  /** Item width in dp, used for snapping. */
  itemWidth: number;
  accessibilityLabel?: string;
}

export function HorizontalCarousel<T>({
  data,
  renderItem,
  keyExtractor,
  itemWidth,
  accessibilityLabel,
}: HorizontalCarouselProps<T>) {
  const theme = useTheme();
  const gap = theme.spacing.md;
  return (
    <FlatList
      horizontal
      data={data}
      keyExtractor={keyExtractor}
      renderItem={({ item }) => renderItem(item)}
      showsHorizontalScrollIndicator={false}
      snapToInterval={itemWidth + gap}
      decelerationRate="fast"
      accessibilityRole="list"
      accessibilityLabel={accessibilityLabel}
      contentContainerStyle={{
        gap,
        paddingHorizontal: theme.layout.screenPadding,
        paddingBottom: theme.spacing.xxs,
      }}
    />
  );
}
