import { useEffect } from 'react';

import i18n, { isLanguageAvailable } from '../../i18n';
import { useAppState } from '../../state';

/** Keeps i18next in step with the language preference; unavailable languages fall back to English. */
export function LanguageSync() {
  const { preferences } = useAppState();
  useEffect(() => {
    const language = isLanguageAvailable(preferences.language) ? preferences.language : 'en';
    if (i18n.language !== language) {
      void i18n.changeLanguage(language);
    }
  }, [preferences.language]);
  return null;
}
