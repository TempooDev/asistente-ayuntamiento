/** Chat session as listed by the hub (GetSessions). */
export interface ChatSessionSummaryDto {
  id: string;
  createdAt: string;
  preview: string;
  messageCount: number;
}

/** Stored message as returned by the hub (LoadSession). */
export interface ChatMessageDto {
  role: string;
  content: string;
  createdAt: string;
}

/**
 * Chunk of an Arena stream: the battle id first, then text of either side. A side that failed ends with a chunk
 * carrying its typed `errorCode` (`INTERNAL` or `LLM_*`) and no text.
 */
export interface ArenaStreamChunk {
  option: 'Alfa' | 'Beta' | 'SessionId';
  content: string;
  errorCode?: string | null;
}

export type ArenaSide = 'Alfa' | 'Beta';
export type ArenaVote = ArenaSide | 'Tie';

interface MessageBase {
  /** Stable id for tracking in lists (client-side only). */
  id: string;
}

export interface UserMessage extends MessageBase {
  kind: 'user';
  text: string;
}

export interface AssistantMessage extends MessageBase {
  kind: 'assistant';
  text: string;
  /** Set when the answer comes from a resolved Arena battle. */
  arenaWinner?: ArenaVote;
}

export interface ArenaMessage extends MessageBase {
  kind: 'arena';
  battleId?: string;
  alfa: string;
  beta: string;
  /** Sides that failed. A battle with a failed side is not stored by the server and cannot be voted on. */
  failed?: readonly ArenaSide[];
}

export type ChatMessage = UserMessage | AssistantMessage | ArenaMessage;

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';
