import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { StarRating } from './StarRating';

describe('<StarRating />', () => {
  it('tells a screen reader the rating in words when it only shows one', async () => {
    await renderWithProviders(<StarRating value={4} />);

    expect(screen.getByLabelText('4 stars')).toBeOnTheScreen();
    expect(screen.getAllByText('★')).toHaveLength(4);
    expect(screen.getAllByText('☆')).toHaveLength(1);
  });

  it('says "1 star" in the singular', async () => {
    await renderWithProviders(<StarRating value={1} />);

    expect(screen.getByLabelText('1 star')).toBeOnTheScreen();
  });

  it('offers five options, marks the chosen one, and reports a choice', async () => {
    const onChange = jest.fn();
    const user = userEvent.setup();
    await renderWithProviders(<StarRating value={2} onChange={onChange} />);

    expect(screen.getAllByRole('radio')).toHaveLength(5);
    expect(screen.getByRole('radio', { name: '2 stars' })).toBeChecked();
    expect(screen.getByRole('radio', { name: '3 stars' })).not.toBeChecked();

    await user.press(screen.getByRole('radio', { name: '5 stars' }));

    expect(onChange).toHaveBeenCalledWith(5);
  });
});
