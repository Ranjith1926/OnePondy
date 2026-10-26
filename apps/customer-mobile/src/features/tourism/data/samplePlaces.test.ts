import { filterPlaces, samplePlaces, type PlaceSummary } from './samplePlaces';

const places: PlaceSummary[] = [
  { id: 'a', name: 'Promenade Beach', categoryId: 'beach', areaId: 'whiteTown', isSample: true },
  { id: 'b', name: 'Matrimandir', categoryId: 'spiritual', areaId: 'auroville', isSample: true },
];
const labels = (place: PlaceSummary) => [place.areaId === 'auroville' ? 'Auroville' : 'White Town'];

describe('filterPlaces', () => {
  it('matches names case-insensitively', () => {
    expect(filterPlaces(places, 'BEACH', labels).map((p) => p.id)).toEqual(['a']);
  });

  it('matches extra labels such as the area name', () => {
    expect(filterPlaces(places, 'auro', labels).map((p) => p.id)).toEqual(['b']);
  });

  it('returns nothing for a blank query', () => {
    expect(filterPlaces(places, '   ', labels)).toEqual([]);
  });
});

describe('samplePlaces', () => {
  it('is always flagged as sample content', () => {
    expect(samplePlaces.length).toBeGreaterThan(0);
    expect(samplePlaces.every((place) => place.isSample)).toBe(true);
  });

  it('has unique ids', () => {
    expect(new Set(samplePlaces.map((place) => place.id)).size).toBe(samplePlaces.length);
  });
});
