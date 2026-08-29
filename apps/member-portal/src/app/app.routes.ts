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
  {
    path: 'issues',
    title: 'Social Issues - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/issues/issues-list.component').then((m) => m.IssuesListComponent),
  },
  {
    path: 'issues/:id',
    title: 'Social Issue - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/issues/issue-detail.component').then((m) => m.IssueDetailComponent),
  },
  {
    path: 'members',
    title: 'Members - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/members/members-list.component').then((m) => m.MembersListComponent),
  },
  {
    path: 'members/:id',
    title: 'Member - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/members/member-detail.component').then((m) => m.MemberDetailComponent),
  },
  {
    path: 'family',
    title: 'My Family - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/members/family.component').then((m) => m.FamilyComponent),
  },
  {
    path: 'voting',
    title: 'Celebrities of Samaaj - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/voting/campaigns-list.component').then((m) => m.CampaignsListComponent),
  },
  {
    path: 'voting/:id',
    title: 'Campaign - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/voting/campaign-detail.component').then((m) => m.CampaignDetailComponent),
  },
  {
    path: 'pathshala',
    title: 'Jain Pathshala - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/pathshala/pathshala-list.component').then(
        (m) => m.PathshalaListComponent,
      ),
  },
  {
    path: 'pathshala/:id',
    title: 'Pathshala enrolment - samaajconnect',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/pathshala/enrolment.component').then((m) => m.EnrolmentComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'home' },

  // Anything unrecognised goes to Home, which sends signed-out visitors to
  // Login via the guard. A dedicated 404 screen is not in the wireframes.
  { path: '**', redirectTo: 'home' },
];
