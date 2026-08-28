/**
 * Angular ships its packages partially compiled, so anything that touches an
 * injectable outside the Angular build pipeline needs the JIT compiler present.
 * The app's own specs get this from `ng test`; these framework-light library
 * specs load it here.
 */
import '@angular/compiler';
