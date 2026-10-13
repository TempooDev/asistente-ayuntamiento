export interface ArenaCompareRequest {
  query: string;
}

export interface ArenaCompareResponse {
  sessionId: string;
  optionAlfa: string;
  optionBeta: string;
  latencyAlfaMs: number;
  latencyBetaMs: number;
  sourcesAlfa: string[];
  sourcesBeta: string[];
}

export interface ArenaVoteRequest {
  sessionId: string;
  winner: string; // 'ALFA', 'BETA', 'TIE', 'BOTH_BAD'
  clarityReason?: string;
  precisionReason?: string;
  optionalComment?: string;
}

export interface ArenaVoteResponse {
  alfaSystem: string;
  betaSystem: string;
}
