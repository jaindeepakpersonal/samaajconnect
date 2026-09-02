import { Routes } from '@angular/router';
import { authGuard } from '@samaajconnect/shared';
import { currentUserGuard } from './core/current-user.guard';
import { ShellComponent } from './shell/shell.component';

/**
 * The wireframe's left nav, as routes. Screens whose backend does not exist yet
 * are not routed at all - the nav lists them disabled with a reason, which is
 * more honest than a route that lands on an empty page.
 *
 * `authGuard` is a UX convenience only. Every endpoint behind these screens
 * re-checks roles and permissions server-side, and that check is the
 * authorization boundary (root `CLAUDE.md` §7).
 */
export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in - samaajconnect admin',
    loadComponent: () => import('./features/auth/login.component').then((m) => m.AdminLoginComponent),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard, currentUserGuard],
    children: [
      {
        path: 'dashboard',
        title: 'Dashboard - samaajconnect admin',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'tenants',
        title: 'Samaaj - samaajconnect admin',
        loadComponent: () =>
          import('./features/tenants/tenant-list.component').then((m) => m.TenantListComponent),
      },
      {
        path: 'tenants/new',
        title: 'Create Samaaj - samaajconnect admin',
        loadComponent: () =>
          import('./features/tenants/create-tenant.component').then((m) => m.CreateTenantComponent),
      },
      {
        path: 'admins',
        title: 'Admin users - samaajconnect admin',
        loadComponent: () =>
          import('./features/admins/admin-list.component').then((m) => m.AdminListComponent),
      },
      {
        path: 'admins/invite',
        title: 'Invite admin - samaajconnect admin',
        loadComponent: () =>
          import('./features/admins/invite-admin.component').then((m) => m.InviteAdminComponent),
      },
      {
        path: 'roles',
        title: 'Role matrix - samaajconnect admin',
        loadComponent: () =>
          import('./features/admins/role-matrix.component').then((m) => m.RoleMatrixComponent),
      },
      {
        path: 'conversions',
        title: 'Conversion queue - samaajconnect admin',
        loadComponent: () =>
          import('./features/conversions/conversion-queue.component').then(
            (m) => m.ConversionQueueComponent,
          ),
      },
      {
        path: 'audit',
        title: 'Audit logs - samaajconnect admin',
        loadComponent: () =>
          import('./features/audit/audit-log.component').then((m) => m.AuditLogComponent),
      },
      {
        path: 'pathshala',
        title: 'Jain Pathshala - samaajconnect admin',
        loadComponent: () =>
          import('./features/pathshala/pathshala-list.component').then(
            (m) => m.PathshalaListComponent,
          ),
      },
      {
        path: 'pathshala/:id',
        title: 'Jain Pathshala - samaajconnect admin',
        loadComponent: () =>
          import('./features/pathshala/pathshala-detail.component').then(
            (m) => m.PathshalaDetailComponent,
          ),
      },
      {
        path: 'pathshala/:id/classes/:classId',
        title: 'Class - samaajconnect admin',
        loadComponent: () =>
          import('./features/pathshala/class-detail.component').then(
            (m) => m.ClassDetailComponent,
          ),
      },
      {
        path: 'boli',
        title: 'Auctions / Boli - samaajconnect admin',
        loadComponent: () =>
          import('./features/boli/boli-list.component').then((m) => m.BoliListComponent),
      },
      {
        path: 'boli/:id',
        title: 'Occasion - samaajconnect admin',
        loadComponent: () =>
          import('./features/boli/occasion-detail.component').then(
            (m) => m.OccasionDetailComponent,
          ),
      },
      {
        path: 'events',
        title: 'Events - samaajconnect admin',
        loadComponent: () =>
          import('./features/events/events-list.component').then((m) => m.EventsListComponent),
      },
      {
        path: 'events/:id',
        title: 'Event - samaajconnect admin',
        loadComponent: () =>
          import('./features/events/event-detail.component').then((m) => m.EventDetailComponent),
      },
      {
        path: 'voting',
        title: 'Celebrities / Voting - samaajconnect admin',
        loadComponent: () =>
          import('./features/voting/campaign-list.component').then(
            (m) => m.CampaignListComponent,
          ),
      },
      {
        path: 'voting/:id',
        title: 'Campaign - samaajconnect admin',
        loadComponent: () =>
          import('./features/voting/campaign-detail.component').then(
            (m) => m.CampaignDetailComponent,
          ),
      },
      {
        path: 'groups',
        title: 'Volunteer groups - samaajconnect admin',
        loadComponent: () =>
          import('./features/groups/groups-list.component').then((m) => m.GroupsListComponent),
      },
      {
        path: 'issues',
        title: 'Social issues - samaajconnect admin',
        loadComponent: () =>
          import('./features/issues/issue-queue.component').then((m) => m.IssueQueueComponent),
      },
      {
        path: 'moderation',
        title: 'Content moderation - samaajconnect admin',
        loadComponent: () =>
          import('./features/moderation/moderation-queue.component').then(
            (m) => m.ModerationQueueComponent,
          ),
      },
      {
        path: 'notifications',
        title: 'Notifications - samaajconnect admin',
        loadComponent: () =>
          import('./features/notifications/broadcast.component').then((m) => m.BroadcastComponent),
      },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: '**', redirectTo: 'dashboard' },
    ],
  },
];
