/**
 * Escape closes, and Tab stays inside the dialog - the keyboard half of the
 * modal every screen here shares (`.modal-backdrop`/`.modal-dialog`). Bind it
 * to the dialog element's `(keydown)`.
 *
 * Shared by every dialog written since the Pathshala tabs; the screens that
 * predate it keep their own copy of the same few lines.
 */
export function modalKeydown(event: KeyboardEvent, close: () => void): void {
  if (event.key === 'Escape') {
    event.stopPropagation();
    close();

    return;
  }

  if (event.key !== 'Tab') {
    return;
  }

  const dialog = event.currentTarget as HTMLElement;
  const focusable = dialog.querySelectorAll<HTMLElement>(
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  );

  if (focusable.length === 0) {
    return;
  }

  const first = focusable[0]!;
  const last = focusable[focusable.length - 1]!;

  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

/** Trims, and turns what is left of an empty box into null. */
export function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
