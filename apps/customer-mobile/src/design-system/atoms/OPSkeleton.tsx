import { useEffect, useState } from 'react';
import {
  Animated,
  Easing,
  type DimensionValue,
  type StyleProp,
  type ViewStyle,
} from 'react-native';

import { useReducedMotion } from '../hooks/useReducedMotion';
import { useTheme } from '../theme/ThemeProvider';
import type { RadiusToken } from '../tokens/layout';

interface OPSkeletonProps {
  height: number;
  width?: DimensionValue;
  radius?: RadiusToken;
  style?: StyleProp<ViewStyle>;
}

/** Placeholder block for loading content. Pulses unless the user prefers reduced motion. */
export function OPSkeleton({ height, width = '100%', radius = 'md', style }: OPSkeletonProps) {
  const theme = useTheme();
  const reducedMotion = useReducedMotion();
  const [opacity] = useState(() => new Animated.Value(1));

  useEffect(() => {
    if (reducedMotion) {
      opacity.setValue(1);
      return;
    }
    const pulse = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, {
          toValue: 0.45,
          duration: 700,
          easing: Easing.inOut(Easing.ease),
          useNativeDriver: true,
        }),
        Animated.timing(opacity, {
          toValue: 1,
          duration: 700,
          easing: Easing.inOut(Easing.ease),
          useNativeDriver: true,
        }),
      ]),
    );
    pulse.start();
    return () => pulse.stop();
  }, [opacity, reducedMotion]);

  return (
    <Animated.View
      accessible={false}
      importantForAccessibility="no-hide-descendants"
      testID="skeleton"
      style={[
        {
          height,
          width,
          borderRadius: theme.radii[radius],
          backgroundColor: theme.colors.skeleton,
          opacity,
        },
        style,
      ]}
    />
  );
}
