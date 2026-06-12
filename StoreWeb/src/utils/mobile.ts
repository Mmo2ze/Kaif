/** Matches app.css: bottom nav / phone layout below 768px. */
export function isMobileLayout(): boolean {
  if (typeof window === 'undefined') return false;
  return window.matchMedia('(max-width: 767px)').matches;
}
