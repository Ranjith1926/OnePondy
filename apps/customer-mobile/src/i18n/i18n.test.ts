import i18n, { isLanguageAvailable, languageOptions } from '.';
import en from './locales/en.json';

function collectStrings(value: unknown, path: string[] = []): [string, unknown][] {
  if (typeof value === 'object' && value !== null) {
    return Object.entries(value).flatMap(([key, child]) => collectStrings(child, [...path, key]));
  }
  return [[path.join('.'), value]];
}

describe('i18n', () => {
  it('has no empty English strings', () => {
    const empty = collectStrings(en).filter(
      ([, text]) => typeof text !== 'string' || text.trim() === '',
    );
    expect(empty).toEqual([]);
  });

  it('marks only English as available until translations are reviewed', () => {
    expect(
      languageOptions.filter((option) => option.available).map((option) => option.code),
    ).toEqual(['en']);
    expect(isLanguageAvailable('ta')).toBe(false);
  });

  it('falls back to English for languages without translations', async () => {
    const tamil = i18n.cloneInstance({ lng: 'ta' });
    await tamil.changeLanguage('ta');

    expect(tamil.t('tabs.home')).toBe('Home');
  });

  it('pluralises counts', () => {
    expect(i18n.t('search.resultsCount', { count: 1 })).toBe('1 place');
    expect(i18n.t('search.resultsCount', { count: 3 })).toBe('3 places');
  });
});
