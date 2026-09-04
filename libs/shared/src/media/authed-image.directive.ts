import { HttpClient } from '@angular/common/http';
import {
  Directive,
  ElementRef,
  Input,
  OnDestroy,
  inject,
} from '@angular/core';

/**
 * Puts an image the platform hosts into an `<img>`, fetched with the caller's
 * token.
 *
 * ## Why this exists at all
 *
 * A plain `<img src="/v1/members/x/photo">` is a browser request that carries no
 * `Authorization` header. Both apps keep the access token in `sessionStorage`
 * and attach it in an HTTP interceptor — deliberately, because a cookie would be
 * sent automatically on every request and this platform has no CSRF protection
 * (see member-portal's CLAUDE.md). So the one thing that makes token-in-storage
 * safe is exactly the thing that makes `<img src>` fail: nothing attaches the
 * token for a tag the browser fetches by itself.
 *
 * The alternative was to let photo URLs be unauthenticated but unguessable, and
 * `SECURITY-CHECKLIST.md` rules that out in as many words — "authorization-
 * checked per request, not just obscured by a random URL". A photograph of a
 * member, or of somebody's child, is exactly the case that rule is about.
 *
 * So the fetch goes through `HttpClient`, which means the interceptor attaches
 * the token, the service authorizes the request like any other, and the bytes
 * become an object URL. That URL is revoked when the element goes away or the
 * source changes; a directive is the right shape for this precisely because
 * something has to own that lifetime, and a component template cannot.
 *
 * ## What it costs, and what it does not
 *
 * A directory of a hundred members is a hundred requests — but it would have
 * been a hundred either way, and the service sends a strong ETag, so the second
 * visit is a hundred 304s with no bytes. What it does cost is that a photo
 * cannot render before the token exists, which is correct: an image nobody is
 * authorized to see should not appear.
 *
 * @example
 * ```html
 * <img [scAuthedSrc]="member.photoUrl" alt="" />
 * ```
 */
@Directive({
  selector: 'img[scAuthedSrc]',
  standalone: true,
})
export class AuthedImageDirective implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly element = inject<ElementRef<HTMLImageElement>>(ElementRef);

  private objectUrl: string | null = null;
  private current: string | null = null;

  /**
   * The path to fetch. Null or empty clears the image rather than requesting
   * anything — a member with no photo is the common case, not an error.
   */
  @Input({ required: true })
  set scAuthedSrc(path: string | null | undefined) {
    const next = path?.trim() || null;

    // Angular re-runs an input binding whenever the surrounding view is
    // checked, and refetching a photo on every change detection pass would turn
    // one directory page into an unbounded number of requests.
    if (next === this.current) {
      return;
    }

    this.current = next;
    this.release();

    if (next === null) {
      this.element.nativeElement.removeAttribute('src');
      return;
    }

    this.http.get(next, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        // The element may have been given a different photo, or destroyed,
        // while this was in flight. Adopting a late response would show the
        // wrong person's face, which is worse than showing none.
        if (this.current !== next) {
          return;
        }

        this.objectUrl = URL.createObjectURL(blob);
        this.element.nativeElement.src = this.objectUrl;
      },
      error: () => {
        // A 404 is the ordinary answer for a member with no photo, and a 403 is
        // an answer this screen should respect quietly rather than shout about.
        // Either way there is no image, and the alt text stands in.
        if (this.current === next) {
          this.element.nativeElement.removeAttribute('src');
        }
      },
    });
  }

  ngOnDestroy(): void {
    this.release();
  }

  /**
   * Object URLs are held by the document until revoked, so a directory the user
   * scrolls through would otherwise keep every photo it had ever shown.
   */
  private release(): void {
    if (this.objectUrl !== null) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }
}
