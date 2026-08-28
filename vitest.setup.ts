/**
 * Angular ships its packages partially compiled, so anything that touches an
 * injectable outside the Angular build pipeline needs the JIT compiler present.
 * The app's own specs get this from `ng test`; these library specs load it here.
 */
import '@angular/compiler';
import 'zone.js';
import 'zone.js/testing';

import { getTestBed } from '@angular/core/testing';
import {
  BrowserTestingModule,
  platformBrowserTesting,
} from '@angular/platform-browser/testing';

/**
 * A TestBed environment, so a library spec can exercise the real thing rather
 * than a copy of its logic.
 *
 * This used to be absent, and the cost was not obvious: the auth interceptor's
 * spec re-implemented the interceptor's decision and asserted against that,
 * which passes perfectly while the shipped interceptor does something else. The
 * interceptor now renews expired tokens and retries, which is far too much to
 * keep a second copy of.
 */
getTestBed().initTestEnvironment(BrowserTestingModule, platformBrowserTesting());
