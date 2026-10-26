import { useTranslation } from 'react-i18next';

import { useAppState } from '../../../state';
import { defaultAreaId, isAreaId, type AreaId } from '../serviceAreas';

export function useSelectedArea(): { areaId: AreaId; name: string } {
  const { t } = useTranslation();
  const { location } = useAppState();
  const areaId = isAreaId(location.selectedAreaId) ? location.selectedAreaId : defaultAreaId;
  return { areaId, name: t(`areas.${areaId}`) };
}
