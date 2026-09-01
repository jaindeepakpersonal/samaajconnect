import { Component, ElementRef, OnInit, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';

/**
 * The shell every member screen renders into.
 *
 * It was a bare `<router-outlet />`, which meant the app had **no landmark at
 * all**: nothing on any screen was inside a `<main>`, so assistive technology
 * had no region to jump to and no way to tell the page's content from its
 * furniture. WCAG 2.1 asks for a way to bypass repeated blocks (2.4.1, level
 * A), and the way to provide it is a skip link plus a target worth skipping to.
 *
 * The skip link is the first thing in the tab order and invisible until it has
 * focus — visible-on-focus rather than permanently hidden, because a sighted
 * keyboard user needs to see where the focus went as much as anybody.
 *
 * `tabindex="-1"` on the `<main>` is what makes the link work at all: without
 * it the element is not focusable, the browser moves the viewport and leaves
 * focus behind on the link, and the next Tab goes back to the navigation the
 * member was trying to skip.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `
    <a class="skip-link" href="#main-content">Skip to the main content</a>

    <main id="main-content" tabindex="-1" #main>
      <router-outlet />
    </main>
  `,
})
export class App implements OnInit {
  private readonly router = inject(Router);

  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  /**
   * Moves focus to the new page after every in-app navigation.
   *
   * A full page load resets focus to the top of the document and a screen
   * reader starts reading; a router navigation does neither. Focus stays on
   * whatever was clicked — a tile on Home that no longer exists — so a screen
   * reader announces nothing at all and the next Tab continues from a place
   * that has nothing to do with what is now on screen. The member is told the
   * page changed by sighted layout alone.
   *
   * Focusing the `<main>` is the smallest fix that works: it puts the caret at
   * the top of the new content, so the next Tab is the first control on the
   * page and a screen reader reads from the heading down.
   *
   * The first render is deliberately skipped. The browser has just done this
   * itself, and taking focus on load would fight the ordinary behaviour rather
   * than restore it.
   */
  ngOnInit(): void {
    let first = true;

    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe(() => {
        if (first) {
          first = false;
          return;
        }

        this.main().nativeElement.focus({ preventScroll: true });
      });
  }
}
