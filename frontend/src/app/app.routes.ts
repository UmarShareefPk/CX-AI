import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/guards';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in · CX Insight',
    loadComponent: () => import('./pages/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard · CX Insight',
        loadComponent: () => import('./pages/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'responses',
        title: 'Survey responses · CX Insight',
        loadComponent: () => import('./pages/responses/responses').then((m) => m.Responses),
      },
      {
        path: 'leaderboard',
        canActivate: [adminGuard],
        title: 'Leaderboard · CX Insight',
        loadComponent: () => import('./pages/leaderboard/leaderboard').then((m) => m.LeaderboardPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
