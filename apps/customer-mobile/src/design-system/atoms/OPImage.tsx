import { useState } from 'react';
import {
  Image,
  StyleSheet,
  View,
  type DimensionValue,
  type ImageSourcePropType,
} from 'react-native';

import { useTheme } from '../theme/ThemeProvider';
import type { RadiusToken } from '../tokens/layout';
import { OPIcon, type IconName } from './OPIcon';

interface OPImageProps {
  source?: ImageSourcePropType;
  /** Shown when there is no image or it fails to load. */
  fallbackIcon: IconName;
  aspectRatio?: number;
  width?: DimensionValue;
  height?: DimensionValue;
  radius?: RadiusToken;
  /** Omit for decorative images. */
  accessibilityLabel?: string;
}

export function OPImage({
  source,
  fallbackIcon,
  aspectRatio,
  width,
  height,
  radius = 'md',
  accessibilityLabel,
}: OPImageProps) {
  const theme = useTheme();
  const [failed, setFailed] = useState(false);
  const showFallback = !source || failed;

  return (
    <View
      accessible={accessibilityLabel !== undefined}
      accessibilityRole={accessibilityLabel !== undefined ? 'image' : undefined}
      accessibilityLabel={accessibilityLabel}
      style={[
        styles.frame,
        {
          aspectRatio,
          width,
          height,
          borderRadius: theme.radii[radius],
          backgroundColor: theme.colors.brand.primaryContainer,
        },
      ]}
    >
      {showFallback ? (
        <View style={styles.fallback} testID="image-fallback">
          <OPIcon name={fallbackIcon} size={32} color={theme.colors.brand.onPrimaryContainer} />
        </View>
      ) : (
        <Image
          source={source}
          resizeMode="cover"
          style={StyleSheet.absoluteFill}
          onError={() => setFailed(true)}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  frame: {
    overflow: 'hidden',
  },
  fallback: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
