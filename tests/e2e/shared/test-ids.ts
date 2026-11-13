/**
 * Stable test selectors — use data-testid attributes for E2E targeting.
 * Add to HTML as: [attr.data-testid]="'staff-list-table'"
 * Select in tests as: page.getByTestId('staff-list-table')
 */
export const TestIds = {
  // Navigation
  NAV_STAFF: 'nav-staff',
  NAV_DASHBOARD: 'nav-dashboard',
  NAV_LEAVE: 'nav-leave',

  // Staff
  STAFF_TABLE: 'staff-list-table',
  STAFF_ADD_BTN: 'staff-add-btn',
  STAFF_SEARCH: 'staff-search',

  // Auth
  LOGIN_STAFF_ID: 'login-staff-id',
  LOGIN_PASSWORD: 'login-password',
  LOGIN_SUBMIT: 'login-submit',
} as const;
