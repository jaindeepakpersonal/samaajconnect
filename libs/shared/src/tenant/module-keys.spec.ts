import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { AllModuleKeys, ModuleKeys, hasModule } from './module-keys';

/**
 * The module keys, and whether they are the ones the platform actually uses.
 *
 * This file exists because of what a wrong key does: nothing. A screen
 * filtering on a key the catalogue has never heard of does not fail — the
 * filter simply never matches, so the feature is missing from the portal for
 * every Samaaj, forever, with nothing logged. That is not hypothetical. Home
 * filtered its Events and Volunteer tiles on `Events` and `VolunteerGroups`,
 * neither of which is a module key, and both tiles were invisible to everybody
 * until somebody noticed by eye.
 *
 * So the interesting test here is not `hasModule`'s logic. It is the last one:
 * comparing these constants against the gateway's own route metadata, which is
 * the only check in the repository that can fail when the two drift apart.
 */
describe('ModuleKeys', () => {
  it('matches every module the gateway gates a route on', () => {
    // The gateway is the enforcement point: a route carries `Metadata.module`
    // and `ModuleGateMiddleware` answers 404 when the Samaaj does not run it.
    // A key here that no route names is a filter that hides a feature nobody
    // can switch on, and that is exactly the bug this file was written after.
    const config = readFileSync(
      join(process.cwd(), 'gateway/src/Sangam.Gateway/appsettings.json'),
      'utf8',
    );

    const gatewayKeys = [
      ...new Set(
        [...config.matchAll(/"module"\s*:\s*"([^"]+)"/g)].map((match) => match[1]!.toLowerCase()),
      ),
    ].sort();

    expect(gatewayKeys.length).toBeGreaterThan(0);
    expect([...AllModuleKeys].sort()).toEqual(gatewayKeys);
  });

  it('lists every key it declares', () => {
    expect([...AllModuleKeys].sort()).toEqual([...Object.values(ModuleKeys)].sort());
  });

  it('uses the lowercase-hyphenated form the gateway compares against', () => {
    for (const key of AllModuleKeys) {
      expect(key).toBe(key.toLowerCase());
      expect(key).toMatch(/^[a-z]+(-[a-z]+)*$/);
    }
  });
});

describe('hasModule', () => {
  it('finds a module a Samaaj runs', () => {
    expect(hasModule(['community', 'pathshala'], ModuleKeys.Pathshala)).toBe(true);
  });

  it('does not find one it does not run', () => {
    expect(hasModule(['community'], ModuleKeys.Boli)).toBe(false);
  });

  it('matches however the key was cased', () => {
    // Belt and braces rather than a rule anything depends on — a Samaaj's keys
    // are canonicalised on the way in — but it matches what the gateway does.
    expect(hasModule(['Community', 'PATHSHALA'], ModuleKeys.Pathshala)).toBe(true);
  });

  it('treats an unknown module list as nothing enabled', () => {
    // The Samaaj lookup failing must not silently enable everything, and must
    // not throw either: Home renders without the filter rather than not at all.
    expect(hasModule(undefined, ModuleKeys.Community)).toBe(false);
    expect(hasModule([], ModuleKeys.Community)).toBe(false);
  });

  it('does not match a key that merely contains the module name', () => {
    expect(hasModule(['community-extra'], ModuleKeys.Community)).toBe(false);
  });
});
