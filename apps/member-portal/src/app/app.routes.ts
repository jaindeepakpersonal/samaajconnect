import { Routes } from '@angular/router';
import { authGuard } from '@samaajconnect/shared';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Login - samaajconnect',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    title: 'Register - samaajconnect',
    loadComponent: () =>
      import('./features/auth/register.component').then((m) => m.RegisterComponent),
  },
  {
    path: 'home',
    title: 'Home - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () => import('./features/home/home.component').then((m) => m.HomeComponent),
  },
  {
    path: 'timeline',
    title: 'Timeline - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/timeline/timeline.component').then((m) => m.TimelineComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'home' },

  // Anything unrecognised goes to Home, which sends signed-out visitors to
  // Login via the guard. A dedicated 404 screen is not in the wireframes.
  { path: '**', redirectTo: 'home' },
];
