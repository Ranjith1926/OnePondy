import { defaultAreaId, isAreaId, serviceAreaIds, type AreaId } from '../location/serviceAreas';

/**
 * The specific areas a restaurant delivers to. A restaurant that lists the whole city serves every area; an order always names one
 * specific area, never the whole city, so this is what the customer chooses from.
 */
export function servedAreas(deliveryAreas: readonly string[]): AreaId[] {
  const specific = serviceAreaIds.filter((id) => id !== defaultAreaId);
  if (deliveryAreas.includes(defaultAreaId)) {
    return specific;
  }

  return specific.filter((id) => deliveryAreas.includes(id));
}

/** The area to start from: where the customer already said they are, if the restaurant delivers there; else its first area. */
export function startingArea(
  selectedAreaId: string | null,
  served: readonly AreaId[],
): AreaId | null {
  if (isAreaId(selectedAreaId) && served.includes(selectedAreaId)) {
    return selectedAreaId;
  }

  return served[0] ?? null;
}
