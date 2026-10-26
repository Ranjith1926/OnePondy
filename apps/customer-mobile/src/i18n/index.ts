import { createInstance } from 'i18next';
import { initReactI18next } from 'react-i18next';

import type { Language } from '../state';
import en from './locales/en.json';

export interface LanguageOption {
  code: Language;
  /** The language's own name; the same in every locale. */
  nativeName: string;
  /** False until reviewed translations exist (spec D10). */
  available: boolean;
}

export const languageOptions: readonly LanguageOption[] = [
  { code: 'en', nativeName: 'English', available: true },
  { code: 'ta', nativeName: 'தமிழ்', available: false },
  { code: 'fr', nativeName: 'Français', available: false },
];

export function isLanguageAvailable(language: Language): boolean {
  return languageOptions.some((option) => option.code === language && option.available);
}

// A dedicated instance, registered with react-i18next so useTranslation() uses it.
const i18n = createInstance();

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
  },
  lng: 'en',
  // Tamil and French fall back to English until reviewed translations exist (spec §10, Phase 1).
  fallbackLng: 'en',
  supportedLngs: languageOptions.map((option) => option.code),
  interpolation: {
    // React already escapes rendered values.
    escapeValue: false,
  },
  returnNull: false,
  initAsync: false,
});

export default i18n;
