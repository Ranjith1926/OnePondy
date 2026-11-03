import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react-native';

import { ApiError } from '../../../api/apiErrors';
import { renderWithProviders } from '../../../test-utils/render';
import { aPublicReview, aPublicReviewPage } from '../../../test-utils/reviewFixture';
import { reviewsApi } from '../api/reviewsApi';
import { PublicReviews } from './PublicReviews';
import { RatingLine } from './RatingLine';

jest.mock('../api/reviewsApi', () => ({ reviewsApi: { listPublic: jest.fn() } }));

const listPublic = reviewsApi.listPublic as jest.Mock;

async function renderReviews() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <PublicReviews subject={{ listingId: 'listing-1' }} />
    </QueryClientProvider>,
  );
}

beforeEach(() => listPublic.mockReset());

describe('<PublicReviews />', () => {
  it('asks for the reviews of the subject it was given', async () => {
    listPublic.mockResolvedValue(aPublicReviewPage());

    await renderReviews();

    await screen.findByTestId('public-reviews');
    expect(listPublic).toHaveBeenCalledWith({ listingId: 'listing-1' });
  });

  it('shows the average and each review with its author and the business reply', async () => {
    listPublic.mockResolvedValue(
      aPublicReviewPage({
        rating: { average: 4.3, count: 3 },
        items: [
          aPublicReview({
            reply: { text: 'Thank you, Anita!', repliedAt: '2026-11-02T08:00:00Z' },
          }),
          aPublicReview({ id: 'r2', rating: 3, text: 'Good, a little late.', authorName: 'Ravi' }),
          aPublicReview({ id: 'r3', rating: 5, text: null, authorName: 'A customer' }),
        ],
      }),
    );

    await renderReviews();

    expect(await screen.findByText('4.3 · 3 reviews')).toBeOnTheScreen();
    expect(screen.getAllByTestId('review-card')).toHaveLength(3);
    expect(screen.getByText('Wonderful trip, would go again.')).toBeOnTheScreen();
    expect(screen.getByText('Reply from the business')).toBeOnTheScreen();
    expect(screen.getByText('Thank you, Anita!')).toBeOnTheScreen();
    expect(screen.getByText(/^Ravi, /)).toBeOnTheScreen();
  });

  it('shows only a count, not an average, while there are too few reviews', async () => {
    listPublic.mockResolvedValue(aPublicReviewPage({ rating: null, totalCount: 2 }));

    await renderReviews();

    expect(await screen.findByText('2 reviews')).toBeOnTheScreen();
    expect(screen.queryByTestId('rating-line')).not.toBeOnTheScreen();
  });

  it('says there are no reviews yet', async () => {
    listPublic.mockResolvedValue(aPublicReviewPage({ items: [], totalCount: 0 }));

    await renderReviews();

    expect(await screen.findByText('No reviews yet.')).toBeOnTheScreen();
  });

  it('is simply not there if the reviews cannot be loaded', async () => {
    listPublic.mockRejectedValue(new ApiError(500, 'http_500'));

    await renderReviews();

    expect(await screen.queryByTestId('public-reviews')).not.toBeOnTheScreen();
  });
});

describe('<RatingLine />', () => {
  it('reads out the rating in words', async () => {
    await renderWithProviders(<RatingLine rating={{ average: 4.0, count: 1 }} />);

    expect(screen.getByText('4.0 · 1 review')).toBeOnTheScreen();
    expect(screen.getByLabelText('Rated 4.0 out of 5 from 1 review')).toBeOnTheScreen();
  });

  it('shows nothing without a rating', async () => {
    await renderWithProviders(<RatingLine rating={null} />);

    expect(screen.queryByTestId('rating-line')).not.toBeOnTheScreen();
  });
});
