/** What the app keeps about the signed-in person; everything else is loaded from the API. */
export interface SessionUser {
  id: string;
  displayName: string;
  roles: string[];
}
