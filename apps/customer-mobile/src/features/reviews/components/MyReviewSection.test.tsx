import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { ApiError } from '../../../api/apiErrors';
import { bookingId } from '../../../test-utils/bookingFixture';
import { orderId } from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { aMyReview, reviewId } from '../../../test-utils/reviewFixture';
import { signedInState } from '../../../test-utils/signedIn';
import { reviewsApi } from '../api/reviewsApi';
import { MyReviewSection, type ReviewTransaction } from './MyReviewSection';

jest.mock('../api/reviewsApi', () => ({
  reviewsApi: { mine: jest.fn(), create: jest.fn(), edit: jest.fn(), remove: jest.fn() },
}));

const mine = reviewsApi.mine as jest.Mock;
const create = reviewsApi.create as jest.Mock;
const edit = reviewsApi.edit as jest.Mock;
const remove = reviewsApi.remove as jest.Mock;

const booking: ReviewTransaction = { kind: 'booking', id: bookingId };
const order: ReviewTransaction = { kind: 'foodOrder', id: orderId };

async function renderSection(transaction: ReviewTransaction = booking, eligible = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <MyReviewSection transaction={transaction} eligible={eligible} />
    </QueryClientProvider>,
    { initialState: signedInState },
  );
}

beforeEach(() => {
  mine.mockReset();
  create.mockReset();
  edit.mockReset();
  remove.mockReset();
});

describe('<MyReviewSection /> before a review is written', () => {
  it('shows nothing, and asks the server nothing, while the booking or order is not finished', async () => {
    await renderSection(booking, false);

    expect(screen.queryByTestId('write-review')).not.toBeOnTheScreen();
    expect(mine).not.toHaveBeenCalled();
  });

  it('invites the customer to write one', async () => {
    mine.mockResolvedValue([]);

    await renderSection();

    expect(await screen.findByText('Tell others how it went.')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Write a review' })).toBeOnTheScreen();
  });

  it('needs a rating before it sends anything', async () => {
    mine.mockResolvedValue([]);
    const user = userEvent.setup();
    await renderSection();

    await user.press(await screen.findByRole('button', { name: 'Write a review' }));
    await user.press(screen.getByTestId('review-submit'));

    expect(screen.getByText('Choose a rating from 1 to 5 stars.')).toBeOnTheScreen();
    expect(create).not.toHaveBeenCalled();
  });

  it('sends the rating and words for a booking', async () => {
    mine.mockResolvedValue([]);
    create.mockResolvedValue(aMyReview());
    const user = userEvent.setup();
    await renderSection();

    await user.press(await screen.findByRole('button', { name: 'Write a review' }));
    await user.press(screen.getByRole('radio', { name: '4 stars' }));
    await user.type(screen.getByLabelText('What did you think? (optional)'), '  Lovely trip.  ');
    await user.press(screen.getByTestId('review-submit'));

    expect(create).toHaveBeenCalledWith({ bookingId, rating: 4, text: 'Lovely trip.' });
  });

  it('sends a food order review without words when none are given', async () => {
    mine.mockResolvedValue([]);
    create.mockResolvedValue(aMyReview({ bookingId: null, foodOrderId: orderId }));
    const user = userEvent.setup();
    await renderSection(order);

    await user.press(await screen.findByRole('button', { name: 'Write a review' }));
    await user.press(screen.getByRole('radio', { name: '5 stars' }));
    await user.press(screen.getByTestId('review-submit'));

    expect(create).toHaveBeenCalledWith({ foodOrderId: orderId, rating: 5, text: undefined });
  });

  it('tells the customer when the time to review has passed', async () => {
    mine.mockResolvedValue([]);
    create.mockRejectedValue(new ApiError(409, 'review_window_closed'));
    const user = userEvent.setup();
    await renderSection();

    await user.press(await screen.findByRole('button', { name: 'Write a review' }));
    await user.press(screen.getByRole('radio', { name: '3 stars' }));
    await user.press(screen.getByTestId('review-submit'));

    expect(await screen.findByText('The time to review this has passed.')).toBeOnTheScreen();
  });

  it('ignores a review of some other booking or order', async () => {
    mine.mockResolvedValue([aMyReview({ bookingId: 'someone-elses', foodOrderId: null })]);

    await renderSection();

    expect(await screen.findByRole('button', { name: 'Write a review' })).toBeOnTheScreen();
    expect(screen.queryByTestId('my-review')).not.toBeOnTheScreen();
  });
});

describe('<MyReviewSection /> once a review exists', () => {
  it('shows a waiting review with what the customer wrote and that only they can see it', async () => {
    mine.mockResolvedValue([aMyReview()]);

    await renderSection();

    expect(await screen.findByTestId('my-review')).toBeOnTheScreen();
    expect(screen.getByLabelText('4 stars')).toBeOnTheScreen();
    expect(screen.getByText('Lovely morning on the water.')).toBeOnTheScreen();
    expect(
      screen.getByText('Waiting to be checked. Only you can see it until it is.'),
    ).toBeOnTheScreen();
    expect(screen.getByText('You can change this review once.')).toBeOnTheScreen();
  });

  it('shows the business reply under a published review', async () => {
    mine.mockResolvedValue([
      aMyReview({
        status: 'published',
        reply: { text: 'Thank you for coming!', repliedAt: '2026-11-03T08:00:00Z' },
      }),
    ]);

    await renderSection();

    expect(await screen.findByText('Published.')).toBeOnTheScreen();
    expect(screen.getByText('Thank you for coming!')).toBeOnTheScreen();
  });

  it('changes the review once, sending the version it was looking at', async () => {
    mine.mockResolvedValue([aMyReview({ version: 7 })]);
    edit.mockResolvedValue(aMyReview({ rating: 2, canEdit: false }));
    const user = userEvent.setup();
    await renderSection();

    await user.press(await screen.findByRole('button', { name: 'Change review' }));
    await user.press(screen.getByRole('radio', { name: '2 stars' }));
    await user.press(screen.getByTestId('review-submit'));

    expect(edit).toHaveBeenCalledWith(reviewId, {
      version: 7,
      rating: 2,
      text: 'Lovely morning on the water.',
    });
  });

  it('offers no change once the one change is used or its time has passed', async () => {
    mine.mockResolvedValue([aMyReview({ canEdit: false })]);

    await renderSection();

    await screen.findByTestId('my-review');
    expect(screen.queryByRole('button', { name: 'Change review' })).not.toBeOnTheScreen();
    expect(screen.queryByText('You can change this review once.')).not.toBeOnTheScreen();
  });

  it('asks before deleting, then deletes', async () => {
    mine.mockResolvedValue([aMyReview({ status: 'published' })]);
    remove.mockResolvedValue(undefined);
    const user = userEvent.setup();
    await renderSection();

    await user.press(await screen.findByRole('button', { name: 'Delete review' }));
    expect(screen.getByText('Delete this review?')).toBeOnTheScreen();
    expect(remove).not.toHaveBeenCalled();
    await user.press(screen.getAllByRole('button', { name: 'Delete review' }).at(-1)!);

    expect(remove).toHaveBeenCalledWith(reviewId);
  });

  it('shows why a review was not published, and offers neither a change nor a delete', async () => {
    mine.mockResolvedValue([
      aMyReview({
        status: 'rejected',
        moderationReason: 'Please leave out phone numbers.',
        canEdit: false,
      }),
    ]);

    await renderSection();

    expect(await screen.findByText('This review was not published.')).toBeOnTheScreen();
    expect(screen.getByText('Not published: Please leave out phone numbers.')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Change review' })).not.toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Delete review' })).not.toBeOnTheScreen();
  });

  it('says a taken-down review was taken down', async () => {
    mine.mockResolvedValue([
      aMyReview({ status: 'removed', moderationReason: 'Reported as abusive.', canEdit: false }),
    ]);

    await renderSection();

    expect(await screen.findByText('This review was taken down.')).toBeOnTheScreen();
  });
});
