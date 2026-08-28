/**
 * Shared surface for both Angular apps (root CLAUDE.md section 7). Anything
 * imported by member-portal and admin-portal alike belongs here; anything used
 * by exactly one app does not.
 */
export * from './api/api-config';
export * from './api/problem-details';
export * from './auth/auth.guard';
export * from './auth/auth.interceptor';
export * from './auth/auth.models';
export * from './auth/auth.service';
export * from './auth/token.store';
export * from './tenant/tenant.interceptor';
export * from './tenant/tenant.service';
