import { useTranslation } from 'react-i18next';

import { TileMap, type MapMarker, type LatLng } from '../../../design-system';
import { attributionUrl, tileSource } from '../../../maps/mapConfig';

interface AppMapProps {
  markers: readonly MapMarker[];
  /** Spoken name of the map, which should say what it shows. */
  accessibilityLabel: string;
  height?: number;
  interactive?: boolean;
  viewKey?: string;
  center?: LatLng;
  zoom?: number;
  onMarkerPress?: (id: string) => void;
}

/** The app's map: the design system's tile map with our tile source, the credit and the translated button names filled in. */
export function AppMap(props: AppMapProps) {
  const { t } = useTranslation();
  return (
    <TileMap
      {...props}
      tileSource={tileSource}
      attributionUrl={attributionUrl}
      labels={{
        zoomIn: t('maps.zoomIn'),
        zoomOut: t('maps.zoomOut'),
        attribution: t('maps.attribution'),
        attributionHint: t('maps.attributionHint'),
      }}
    />
  );
}
