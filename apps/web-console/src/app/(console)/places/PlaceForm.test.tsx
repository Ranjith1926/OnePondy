import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import type { PlaceDetail } from '@/lib/apiTypes';

import { PlaceForm } from './PlaceForm';

vi.mock('./actions', () => ({ savePlace: vi.fn() }));

const categories = [
  { code: 'beach', name: 'Beach' },
  { code: 'heritage', name: 'Heritage' },
];

const place = {
  id: '019a3b4c-0000-7000-8000-000000000001',
  version: 7,
  name: 'Paradise Beach',
  categoryCode: 'beach',
  description: 'Reached by boat.',
  visitorGuidance: null,
  address: null,
  latitude: 11.8915,
  longitude: 79.8257,
  openingHours: null,
  entryFees: [{ category: 'standard', amount: 50 }],
  translations: [],
  status: 'verified',
  isPublished: true,
} as unknown as PlaceDetail;

describe('<PlaceForm />', () => {
  it('starts empty for a new place, with labelled fields', () => {
    render(<PlaceForm place={null} categories={categories} />);

    expect(screen.getByLabelText('Name (English)')).toHaveValue('');
    expect(screen.getByLabelText('Category')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Create place' })).toBeEnabled();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('warns that editing a published place takes it off the app', () => {
    const { container } = render(<PlaceForm place={place} categories={categories} />);

    expect(screen.getByRole('status')).toHaveTextContent(
      'takes it off the app until it is verified again',
    );
    expect(screen.getByLabelText('Name (English)')).toHaveValue('Paradise Beach');
    expect(screen.getByLabelText('Standard')).toHaveValue('50');
    expect(container.querySelector('input[name="version"]')).toHaveValue('7');
  });
});
