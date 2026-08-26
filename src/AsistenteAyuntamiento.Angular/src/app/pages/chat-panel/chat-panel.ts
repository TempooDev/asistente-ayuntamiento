import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSessionSummaryDto } from '../../services/chat';
import { marked } from 'marked';
import DOMPurify from 'dompurify';

interface ChatMessage {
  text: string;
  isUser: boolean;
  html?: string;
}

@Component({
  selector: 'app-chat-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './chat-panel.html',
  styleUrl: './chat-panel.scss'
})
export class ChatPanelComponent implements OnInit, OnDestroy {
  private chatService = inject(ChatService);
  
  currentMessage = '';
  isWaitingForResponse = false;
  isGenerating = false;
  messages: ChatMessage[] = [];
  
  sidebarOpen = false;
  isLoadingHistory = false;
  currentSessionId = '';
  sessions: ChatSessionSummaryDto[] = [];
  
  @ViewChild('messagesContainer') private messagesContainer!: ElementRef;

  async ngOnInit() {
    this.chatService.messageReceived$.subscribe((msg) => {
      this.handleIncomingMessage(msg);
    });

    try {
      await this.chatService.connect();
      await this.loadSessions();
      if (this.sessions.length > 0) {
        await this.selectSession(this.sessions[0].id);
      }
    } catch (e) {
      console.error(e);
    }
  }

  ngOnDestroy() {
    // Ideally unsubscribe or disconnect
  }

  get isConnected() {
    return this.chatService.isConnected;
  }

  async loadSessions() {
    this.sessions = await this.chatService.getSessions();
  }

  async selectSession(id: string) {
    this.currentSessionId = id;
    this.sidebarOpen = false;
    this.messages = [];
    this.isLoadingHistory = true;

    try {
      const history = await this.chatService.loadSession(id);
      this.messages = history.map(m => {
        const isUser = m.role.toLowerCase() === 'user';
        return {
          text: m.content,
          isUser,
          html: isUser ? undefined : this.renderMarkdown(m.content)
        };
      });
      this.scrollToBottom();
    } catch (e) {
      console.error(e);
    } finally {
      this.isLoadingHistory = false;
    }
  }

  async createNewChat() {
    this.sidebarOpen = false;
    this.messages = [];
    this.currentMessage = '';
    
    try {
      this.currentSessionId = await this.chatService.createNewSession();
      await this.loadSessions();
    } catch (e) {
      console.error(e);
    }
  }

  toggleSidebar() {
    this.sidebarOpen = !this.sidebarOpen;
  }

  formatDate(dateStr: string) {
    const d = new Date(dateStr);
    const today = new Date();
    if (d.toDateString() === today.toDateString()) {
      return 'Hoy, ' + d.toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'});
    }
    const yesterday = new Date(today);
    yesterday.setDate(yesterday.getDate() - 1);
    if (d.toDateString() === yesterday.toDateString()) {
      return 'Ayer';
    }
    return d.toLocaleDateString('es-ES', { day: '2-digit', month: 'short' });
  }

  renderMarkdown(text: string): string {
    const raw = marked.parse(text) as string;
    return DOMPurify.sanitize(raw);
  }

  scrollToBottom() {
    setTimeout(() => {
      if (this.messagesContainer) {
        const el = this.messagesContainer.nativeElement;
        el.scrollTop = el.scrollHeight;
      }
    }, 50);
  }

  async sendMessage() {
    if (!this.currentMessage.trim() || !this.isConnected || this.isGenerating) return;

    const text = this.currentMessage;
    this.currentMessage = '';
    this.isWaitingForResponse = true;
    this.isGenerating = true;

    if (!this.currentSessionId) {
      await this.createNewChat();
    }

    this.messages.push({ text, isUser: true });
    
    const assistantMsg: ChatMessage = { text: '', isUser: false, html: '' };
    this.messages.push(assistantMsg);
    this.scrollToBottom();

    try {
      const stream = this.chatService.streamMessage(this.currentSessionId, text);
      let firstChunk = true;

      stream.subscribe({
        next: (chunk) => {
          if (firstChunk) {
            this.isWaitingForResponse = false;
            firstChunk = false;
          }
          assistantMsg.text += chunk;
          assistantMsg.html = this.renderMarkdown(assistantMsg.text);
          this.scrollToBottom();
        },
        complete: () => {
          if (firstChunk) this.isWaitingForResponse = false;
          this.isGenerating = false;
          this.loadSessions();
        },
        error: (err) => {
          assistantMsg.text += `\n[Error: ${err.message || err}]`;
          assistantMsg.html = this.renderMarkdown(assistantMsg.text);
          this.isWaitingForResponse = false;
          this.isGenerating = false;
        }
      });
    } catch (e: any) {
      assistantMsg.text += `\n[Error: ${e.message}]`;
      assistantMsg.html = this.renderMarkdown(assistantMsg.text);
      this.isWaitingForResponse = false;
      this.isGenerating = false;
    }
  }

  async handleIncomingMessage(msg: string) {
    this.isWaitingForResponse = false;
    this.messages.push({ text: msg, isUser: false, html: this.renderMarkdown(msg) });
    await this.loadSessions();
    this.scrollToBottom();
  }

  handleKeydown(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }
}
