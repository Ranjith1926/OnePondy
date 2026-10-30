import en from '../../i18n/locales/en.json';

/** The named delivery areas the API uses (the same list as the customer app's area picker). */
export type AreaId = keyof typeof en.areas;

export const isAreaId = (value: string): value is AreaId => Object.hasOwn(en.areas, value);
