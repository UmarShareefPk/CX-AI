export interface Band {
  label: string;
  tone: 'ok' | 'info' | 'warn' | 'bad' | 'none';
}

/** Score bands as defined in the scoring policy (Cx.Core/Policies/scoring-methodology.md). */
export function scoreBand(score: number | null | undefined): Band {
  if (score === null || score === undefined) return { label: 'No surveys', tone: 'none' };
  if (score >= 85) return { label: 'Excellent', tone: 'ok' };
  if (score >= 70) return { label: 'Good', tone: 'info' };
  if (score >= 55) return { label: 'Needs attention', tone: 'warn' };
  return { label: 'Critical', tone: 'bad' };
}

export function toneClass(tone: Band['tone']): string {
  return tone === 'none' ? '' : `tone-${tone}`;
}
