import { defineConfig } from 'vitest/config';

/**
 * Runs the shared library's specs.
 *
 * Separate from `ng test` on purpose: the Angular unit-test builder only
 * collects specs under the application it is given, so `libs/shared` - which
 * exists precisely because two applications share it - would otherwise have
 * nowhere to put its tests. Everything here is framework-light by design;
 * anything needing TestBed belongs in an app's spec instead.
 */
export default defineConfig({
  test: {
    name: 'shared',
    globals: true,
    environment: 'jsdom',
    include: ['libs/**/*.spec.ts'],
    setupFiles: ['vitest.setup.ts'],
  },
});
