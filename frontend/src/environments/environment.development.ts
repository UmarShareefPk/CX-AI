/**
 * DEVELOPMENT settings (used by `ng serve`; swapped in by fileReplacements in angular.json).
 *
 * apiBaseUrl stays empty so requests go to the dev server's own origin, and proxy.conf.json forwards /api to the API.
 * To skip the proxy and call the API directly instead, use 'http://localhost:5080' (its CORS policy already allows http://localhost:4200).
 */
export const environment = {
  production: false,
  apiBaseUrl: '',
};
