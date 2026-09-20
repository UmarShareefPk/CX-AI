/**
 * PRODUCTION settings (used by `ng build`).
 *
 * apiBaseUrl: absolute URL of the API with no trailing slash, e.g. 'https://cx-api.example.com'.
 * Leave it empty ('') when a reverse proxy serves the app and forwards /api to the API on the same origin.
 * If you set an absolute URL, add this app's origin to Cors:AllowedOrigins in the API configuration.
 */
export const environment = {
  production: true,
  apiBaseUrl: '',
};
