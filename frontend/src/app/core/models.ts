export type Role = 'Admin' | 'Dealer';

export interface User {
  id: string;
  username: string;
  displayName: string;
  role: Role;
  dealerId: string | null;
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  user: User;
}

export interface Period {
  start: string;
  end: string;
  days: number;
}

export interface Summary {
  period: Period;
  dealerId: string | null;
  dealerName: string | null;
  surveyCount: number;
  score: number | null;
  previousScore: number | null;
  scoreChange: number | null;
  rank: number | null;
  rankedDealerCount: number;
  topScore: number | null;
  topDealerName: string | null;
  avgRecommendScore: number | null;
  avgConditionScore: number | null;
  recommendBreakdown: Record<string, number>;
  conditionBreakdown: Record<string, number>;
}

export interface TrendPoint {
  date: string;
  score: number;
  surveyCount: number;
}

export interface SurveyRow {
  id: string;
  submittedAt: string;
  dealerId: string;
  dealerName: string;
  customerName: string;
  customerEmail: string;
  vehicle: string;
  recommend: string;
  vehicleCondition: string;
  recommendScore: number;
  conditionScore: number;
  netScore: number;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface LeaderboardRow {
  dealerId: string;
  dealerName: string;
  surveyCount: number;
  score: number | null;
  rank: number | null;
}

export interface Leaderboard {
  period: Period;
  dealers: LeaderboardRow[];
}

export interface Dealer {
  id: string;
  name: string;
  city: string;
  state: string;
  region: string;
}

export interface ModelInfo {
  id: string;
  provider: string;
  model: string;
  displayName: string;
  supportsTools: boolean;
  isLocal: boolean;
  isDefault: boolean;
}

export interface ModelList {
  default: { provider: string; model: string };
  models: ModelInfo[];
}

/** One streamed step of an agent run (server-sent event payload). */
export interface AgentEvent {
  type: 'conversation' | 'text' | 'reasoning' | 'tool_call' | 'tool_result' | 'done' | 'error';
  text?: string;
  conversationId?: string;
  provider?: string;
  model?: string;
  callId?: string;
  toolName?: string;
  arguments?: string;
  result?: string;
  isError?: boolean;
  inputTokens?: number;
  outputTokens?: number;
  elapsedMs?: number;
}

export const RECOMMEND_LABELS: Record<string, string> = {
  HighlyRecommend: 'Highly recommend',
  Recommend: 'Recommend',
  MightRecommend: 'Might recommend',
  NotRecommend: 'Not recommend',
};

export const CONDITION_LABELS: Record<string, string> = {
  AllGood: 'All good',
  PartMissing: 'Part missing',
  Defect: 'Defect',
  PartMissingAndDefect: 'Part missing and defect',
};
