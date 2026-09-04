/**
 * The module keys a Samaaj can switch on.
 *
 * These mirror `ModuleCatalog` in identity-tenant-service's domain, which is
 * the closed list `Tenant.EnabledModules` may contain and the same list the
 * gateway matches a route's `Metadata.module` against.
 *
 * They live here rather than as string literals in a component because of what
 * a wrong one does. A screen that filters on a key the catalogue has never
 * heard of does not fail: the filter simply never matches, and the feature is
 * missing from the portal for every Samaaj, forever, with nothing logged
 * anywhere. That is not hypothetical - Home filtered its Events and Volunteer
 * tiles on `Events` and `VolunteerGroups`, neither of which is a module key,
 * and both tiles were invisible to everybody until this was written down.
 *
 * Adding a module means adding it in three places: `ModuleCatalog`, the
 * gateway route's metadata, and here.
 *
 * `scripts/module-keys.sh` checks that, and CI runs it — `ModuleCatalog` is
 * the source of truth and this list is compared against it, so forgetting one
 * of the three fails rather than going quiet.
 */
export const ModuleKeys = {
  /** Timeline posts, volunteer groups and Samaaj events - all three. */
  Community: 'community',

  SocialIssues: 'social-issues',
  CelebrityVoting: 'celebrity-voting',
  Pathshala: 'pathshala',

  /** Off by default; most Samaaj do not run auctions. */
  Boli: 'boli',
} as const;

export type ModuleKey = (typeof ModuleKeys)[keyof typeof ModuleKeys];

/** Every key the catalogue has, for a screen that needs to check one. */
export const AllModuleKeys: readonly ModuleKey[] = Object.values(ModuleKeys);

/**
 * Whether a Samaaj running <paramref name="enabled"/> has this module on.
 *
 * Compared case-insensitively, matching the gateway. A Samaaj's stored keys are
 * canonicalised on the way in, so this is belt and braces rather than a rule
 * anything depends on.
 */
export function hasModule(
  enabled: readonly string[] | undefined,
  key: ModuleKey,
): boolean {
  return (enabled ?? []).some((module) => module.toLowerCase() === key);
}
