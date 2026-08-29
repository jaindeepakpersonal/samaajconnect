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
  {
    path: 'events',
    title: 'Events - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/events/events-list.component').then((m) => m.EventsListComponent),
  },
  {
    path: 'events/:id',
    title: 'Event - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/events/event-detail.component').then((m) => m.EventDetailComponent),
  },
  {
    path: 'groups',
    title: 'Volunteer Groups - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/groups/groups-list.component').then((m) => m.GroupsListComponent),
  },
  {
    path: 'groups/:id',
    title: 'Volunteer Group - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/groups/group-detail.component').then((m) => m.GroupDetailComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'home' },

  // Anything unrecognised goes to Home, which sends signed-out visitors to
  // Login via the guard. A dedicated 404 screen is not in the wireframes.
  { path: '**', redirectTo: 'home' },
];
