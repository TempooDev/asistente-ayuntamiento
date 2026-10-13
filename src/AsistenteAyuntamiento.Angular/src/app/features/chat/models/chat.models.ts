export interface ChatSessionSummaryDto {
  id: string;
  createdAt: string;
  preview: string;
  messageCount: number;
}

export interface ChatMessageDto {
  role: string;
  content: string;
  createdAt: string;
}

export interface ChatMessage {
  text: string;
  isUser: boolean;
  html?: string;
  isArena?: boolean;
  alfaText?: string;
  alfaHtml?: string;
  betaText?: string;
  betaHtml?: string;
  arenaResolved?: boolean;
  winner?: 'Alfa' | 'Beta' | 'Tie';
  battleId?: string;
}

export interface ArenaStreamChunk {
  option: 'Alfa' | 'Beta' | 'SessionId';
  content: string;
}
