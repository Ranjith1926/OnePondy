import { createInstance } from 'i18next';
import { initReactI18next } from 'react-i18next';

import en from './locales/en.json';

// A dedicated instance, registered with react-i18next so useTranslation() uses it. English only until
// reviewed Tamil and French translations exist (spec D10), as in the customer app.
const i18n = createInstance();

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
  },
  lng: 'en',
  fallbackLng: 'en',
  interpolation: {
    // React already escapes rendered values.
    escapeValue: false,
  },
  returnNull: false,
  initAsync: false,
});

export default i18n;
