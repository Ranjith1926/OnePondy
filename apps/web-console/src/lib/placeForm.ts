import type {
  DayOfWeek,
  OpeningPeriod,
  PlaceDetail,
  PlaceInput,
  VisitorCategory,
} from './apiTypes';
import { dayLabels, days, visitorCategories, visitorCategoryLabels } from './labels';

// Converts the place form's fields to the API's PlaceInput. The API validates everything again;
// this only catches what cannot be sent at all, such as a latitude that is not a number.

export const PERIODS_PER_DAY = 2;
export const translationLanguages = ['ta', 'fr'] as const;

export const periodField = (day: DayOfWeek, index: number, part: 'opens' | 'closes') =>
  `hours-${day}-${index}-${part}`;

export const feeField = (category: VisitorCategory) => `fee-${category}`;

export const translationField = (
  language: string,
  field: 'name' | 'description' | 'visitorGuidance',
) => `${language}-${field}`;

function text(form: FormData, name: string): string | null {
  const value = form.get(name);
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;
}

function parseNumber(value: string | null): number | null | 'invalid' {
  if (value === null) {
    return null;
  }
  const parsed = Number(value.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : 'invalid';
}

export function placeInputFromForm(form: FormData): {
  input: PlaceInput;
  errors: Record<string, string[]>;
} {
  const errors: Record<string, string[]> = {};
  const addError = (field: string, message: string) => {
    (errors[field] ??= []).push(message);
  };

  const latitude = parseNumber(text(form, 'latitude'));
  const longitude = parseNumber(text(form, 'longitude'));
  if (latitude === 'invalid') {
    addError('latitude', 'Enter a number such as 11.9341.');
  }
  if (longitude === 'invalid') {
    addError('longitude', 'Enter a number such as 79.8306.');
  }

  const periods: OpeningPeriod[] = [];
  for (const day of days) {
    for (let index = 0; index < PERIODS_PER_DAY; index++) {
      const opens = text(form, periodField(day, index, 'opens'));
      const closes = text(form, periodField(day, index, 'closes'));
      if (opens && closes) {
        periods.push({ day, opens, closes });
      } else if (opens || closes) {
        addError('openingHours', `${dayLabels[day]}: enter both opening and closing times.`);
      }
    }
  }
  const alwaysOpen = form.get('alwaysOpen') === 'on';
  const hoursNotes = text(form, 'hoursNotes');

  const entryFees: PlaceInput['entryFees'] = [];
  for (const category of visitorCategories) {
    const amount = parseNumber(text(form, feeField(category)));
    if (amount === 'invalid' || (typeof amount === 'number' && amount < 0)) {
      addError(
        'entryFees',
        `${visitorCategoryLabels[category]}: enter an amount in rupees, or leave it empty.`,
      );
    } else if (amount !== null) {
      entryFees.push({ category, amount });
    }
  }

  const translations: PlaceInput['translations'] = [];
  for (const language of translationLanguages) {
    const name = text(form, translationField(language, 'name'));
    const description = text(form, translationField(language, 'description'));
    const visitorGuidance = text(form, translationField(language, 'visitorGuidance'));
    if (name || description || visitorGuidance) {
      translations.push({ language, name: name ?? '', description, visitorGuidance });
    }
  }

  return {
    input: {
      name: text(form, 'name') ?? '',
      categoryCode: text(form, 'categoryCode') ?? '',
      description: text(form, 'description'),
      visitorGuidance: text(form, 'visitorGuidance'),
      address: text(form, 'address'),
      latitude: typeof latitude === 'number' ? latitude : null,
      longitude: typeof longitude === 'number' ? longitude : null,
      openingHours:
        alwaysOpen || periods.length > 0 || hoursNotes
          ? { alwaysOpen, periods, notes: hoursNotes }
          : null,
      entryFees,
      translations,
    },
    errors,
  };
}

/** Values to pre-fill the form with when editing. */
export function formDefaults(place: PlaceDetail | null) {
  const periodsByDay = new Map<DayOfWeek, OpeningPeriod[]>();
  for (const period of place?.openingHours?.periods ?? []) {
    periodsByDay.set(period.day, [...(periodsByDay.get(period.day) ?? []), period]);
  }

  return {
    period: (day: DayOfWeek, index: number, part: 'opens' | 'closes') =>
      periodsByDay.get(day)?.[index]?.[part] ?? '',
    fee: (category: VisitorCategory) =>
      place?.entryFees.find((fee) => fee.category === category)?.amount.toString() ?? '',
    translation: (language: string, field: 'name' | 'description' | 'visitorGuidance') =>
      place?.translations.find((t) => t.language === language)?.[field] ?? '',
  };
}
