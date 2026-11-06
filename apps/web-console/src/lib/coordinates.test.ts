import { formatCoordinate, parseCoordinate, startingPoint } from './coordinates';

const town = { latitude: 11.9416, longitude: 79.8083 };

describe('parseCoordinate', () => {
  it('reads plain decimal degrees, with a comma or a point', () => {
    expect(parseCoordinate('11.9341', 'latitude')).toBe(11.9341);
    expect(parseCoordinate(' 79,8306 ', 'longitude')).toBe(79.8306);
    expect(parseCoordinate('-33', 'latitude')).toBe(-33);
  });

  it('refuses empty text, words and numbers outside the range', () => {
    expect(parseCoordinate('', 'latitude')).toBeNull();
    expect(parseCoordinate('north', 'latitude')).toBeNull();
    expect(parseCoordinate('91', 'latitude')).toBeNull();
    expect(parseCoordinate('181', 'longitude')).toBeNull();
    expect(parseCoordinate('11.9.3', 'latitude')).toBeNull();
  });
});

describe('formatCoordinate', () => {
  it('keeps six decimals', () => {
    expect(formatCoordinate(11.93412345678)).toBe('11.934123');
    expect(formatCoordinate(79.8)).toBe('79.800000');
  });
});

describe('startingPoint', () => {
  it('starts on the typed position when both coordinates are valid', () => {
    expect(startingPoint('11.93', '79.83', town)).toEqual({
      latitude: 11.93,
      longitude: 79.83,
      hasPin: true,
    });
  });

  it('starts on the town, with no pin, when either is missing or wrong', () => {
    expect(startingPoint('', '79.83', town)).toEqual({ ...town, hasPin: false });
    expect(startingPoint('11.93', 'abc', town)).toEqual({ ...town, hasPin: false });
  });
});
