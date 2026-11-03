import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useAppState } from '../../../state';
import {
  reviewsApi,
  type CreateReviewInput,
  type EditReviewInput,
  type ReviewSubject,
} from '../api/reviewsApi';

/** Every review query starts with this, so one invalidation refreshes the author's list and every public page. */
const reviewsKey = ['reviews'] as const;
export const myReviewsQueryKey = [...reviewsKey, 'mine'] as const;
export const publicReviewsQueryKey = (subject: ReviewSubject) =>
  [...reviewsKey, 'public', subject] as const;

/** Only asks while signed in and only when the screen can offer a review: a read before signing in would fail and the failure would stay cached. */
export function useMyReviews(enabled: boolean) {
  const { auth } = useAppState();
  return useQuery({
    queryKey: myReviewsQueryKey,
    queryFn: () => reviewsApi.mine(),
    enabled: enabled && auth.status === 'authenticated',
  });
}

export function usePublicReviews(subject: ReviewSubject) {
  return useQuery({
    queryKey: publicReviewsQueryKey(subject),
    queryFn: () => reviewsApi.listPublic(subject),
  });
}

export function useCreateReview() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateReviewInput) => reviewsApi.create(input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: reviewsKey }),
  });
}

export function useEditReview(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: EditReviewInput) => reviewsApi.edit(id, input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: reviewsKey }),
  });
}

export function useDeleteReview(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => reviewsApi.remove(id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: reviewsKey }),
  });
}
