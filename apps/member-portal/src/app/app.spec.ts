import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { App } from './app';

@Component({ template: '<h1>First</h1>' })
class FirstPage {}

@Component({ template: '<h1>Second</h1>' })
class SecondPage {}

/**
 * The shell's accessibility contract.
 *
 * It was a bare `<router-outlet />`, so the app had no `<main>` at all and no
 * way to skip past anything. These are the two things WCAG 2.1 asks for that a
 * single-page app has to provide for itself.
 */
describe('App shell', () => {
  let fixture: ComponentFixture<App>;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: '', component: FirstPage },
          { path: 'second', component: SecondPage },
        ]),
      ],
    });

    fixture = TestBed.createComponent(App);
    router = TestBed.inject(Router);

    // detectChanges first: that is what runs ngOnInit and subscribes. The
    // navigation to '/' is then the initial one the shell deliberately ignores,
    // which is the same order the real bootstrap has.
    fixture.detectChanges();

    await router.navigateByUrl('/');
    fixture.detectChanges();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('puts every screen inside a main landmark', () => {
    const main = root().querySelector('main');

    expect(main).not.toBeNull();
    expect(main?.querySelector('router-outlet')).not.toBeNull();
  });

  it('offers a skip link before anything else in the tab order', () => {
    const first = root().querySelector('a');

    expect(first?.classList.contains('skip-link')).toBe(true);
    expect(first?.getAttribute('href')).toBe('#main-content');
  });

  it('points the skip link at something that can actually take focus', () => {
    // Without tabindex the target is not focusable: the browser scrolls, focus
    // stays on the link, and the next Tab goes straight back into the
    // navigation the member was trying to skip.
    const main = root().querySelector('main');

    expect(main?.id).toBe('main-content');
    expect(main?.getAttribute('tabindex')).toBe('-1');
  });

  it('moves focus to the new page after a navigation', async () => {
    // A full page load resets focus and a screen reader starts reading. A
    // router navigation does neither, so without this the member is told the
    // page changed by sighted layout alone.
    await router.navigateByUrl('/second');
    fixture.detectChanges();

    expect(document.activeElement).toBe(root().querySelector('main'));
  });

  it('leaves focus alone on the first navigation', async () => {
    // The browser has just loaded the page and put focus at the top itself;
    // taking it again would fight the ordinary behaviour rather than restore
    // it. Proven by the beforeEach, which navigates to '/' after subscribing
    // and must not have moved focus.
    expect(document.activeElement).not.toBe(root().querySelector('main'));
  });
});
