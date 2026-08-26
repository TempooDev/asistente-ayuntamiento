import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSessionSummaryDto } from '../../services/chat';
import { marked } from 'marked';
import DOMPurify from 'dompurify';

DOMPurify.addHook('afterSanitizeAttributes', function(node) {
  if (node.tagName === 'A') {
    node.setAttribute('target', '_blank');
    node.setAttribute('rel', 'noopener noreferrer');
  }
});

interface ChatMessage {
  text: string;
  isUser: boolean;
  html?: string;
}

@Component({
  selector: 'app-chat-panel',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './chat-panel.html',
  styleUrl: './chat-panel.scss'
})
export class ChatPanelComponent implements OnInit, OnDestroy {
  private chatService = inject(ChatService);
  
  // Non-signal for two-way binding with ngModel (though model() is an option, this is simpler)
  currentMessage = '';
  
  // Signals for state exposed directly from ChatService
  isWaitingForResponse = this.chatService.isWaitingForResponse;
  isGenerating = this.chatService.isGenerating;
  messages = this.chatService.messages;
  currentSessionId = this.chatService.currentSessionId;
  sessions = this.chatService.sessions;
  
  sidebarOpen = signal(false);
  isLoadingHistory = signal(false);
  isConnected = signal(false);
  
  @ViewChild('messagesContainer') private messagesContainer!: ElementRef;

  async ngOnInit() {
    this.chatService.messageReceived$.subscribe((msg) => {
      this.handleIncomingMessage(msg);
    });

    try {
      await this.chatService.connect();
      this.isConnected.set(true);
      
      if (this.currentSessionId() && this.messages().length > 0) {
        // Chat is already loaded (user came back from profile)
        this.scrollToBottom();
      } else {
        await this.loadSessions();
        if (this.sessions().length > 0) {
          await this.selectSession(this.sessions()[0].id);
        }
      }
    } catch (e) {
      console.error(e);
      this.isConnected.set(false);
    }
  }

  ngOnDestroy() {
    // Cleanup if needed
  }

  async loadSessions() {
    const data = await this.chatService.getSessions();
    this.sessions.set(data);
  }

  async selectSession(id: string) {
    this.currentSessionId.set(id);
    this.sidebarOpen.set(false);
    this.messages.set([]);
    this.isLoadingHistory.set(true);

    try {
      const history = await this.chatService.loadSession(id);
      const mapped = history.map(m => {
        const isUser = m.role.toLowerCase() === 'user';
        return {
          text: m.content,
          isUser,
          html: isUser ? undefined : this.renderMarkdown(m.content)
        };
      });
      this.messages.set(mapped);
      this.scrollToBottom();
    } catch (e) {
      console.error(e);
    } finally {
      this.isLoadingHistory.set(false);
    }
  }

  async createNewChat() {
    this.sidebarOpen.set(false);
    this.messages.set([]);
    this.currentMessage = '';
    
    try {
      const newId = await this.chatService.createNewSession();
      this.currentSessionId.set(newId);
      await this.loadSessions();
    } catch (e) {
      console.error(e);
    }
  }

  toggleSidebar() {
    this.sidebarOpen.update(v => !v);
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
    return DOMPurify.sanitize(raw, { ADD_ATTR: ['target'] });
  }

  scrollToBottom() {
    setTimeout(() => {
      if (this.messagesContainer) {
        const el = this.messagesContainer.nativeElement;
        el.scrollTop = el.scrollHeight;
      }
    }, 50);
  }

  private currentStreamSub: any = null;

  async sendMessage() {
    if (!this.currentMessage.trim() || !this.isConnected() || this.isGenerating()) return;

    const text = this.currentMessage;
    this.currentMessage = '';
    this.isWaitingForResponse.set(true);
    this.isGenerating.set(true);

    if (!this.currentSessionId()) {
      await this.createNewChat();
    }

    const assistantMsg: ChatMessage = { text: '', isUser: false, html: '' };
    this.messages.update(msgs => [...msgs, { text, isUser: true }, assistantMsg]);
    this.scrollToBottom();

    try {
      if (this.currentStreamSub) {
        this.currentStreamSub.dispose();
        this.currentStreamSub = null;
      }

      const stream = this.chatService.streamMessage(this.currentSessionId(), text);
      let firstChunk = true;

      this.currentStreamSub = stream.subscribe({
        next: (chunk) => {
          if (firstChunk) {
            this.isWaitingForResponse.set(false);
            firstChunk = false;
          }
          assistantMsg.text += chunk;
          assistantMsg.html = this.renderMarkdown(assistantMsg.text);
          // Trigger change detection for array mutation
          this.messages.update(msgs => [...msgs]); 
          this.scrollToBottom();
        },
        complete: () => {
          if (firstChunk) this.isWaitingForResponse.set(false);
          this.isGenerating.set(false);
          this.loadSessions();
        },
        error: (err) => {
          assistantMsg.text += `\n[Error: ${err.message || err}]`;
          assistantMsg.html = this.renderMarkdown(assistantMsg.text);
          this.messages.update(msgs => [...msgs]);
          this.isWaitingForResponse.set(false);
          this.isGenerating.set(false);
        }
      });
    } catch (e: any) {
      assistantMsg.text += `\n[Error: ${e.message}]`;
      assistantMsg.html = this.renderMarkdown(assistantMsg.text);
      this.messages.update(msgs => [...msgs]);
      this.isWaitingForResponse.set(false);
      this.isGenerating.set(false);
    }
  }

  async handleIncomingMessage(msg: string) {
    this.isWaitingForResponse.set(false);
    this.messages.update(msgs => [...msgs, { text: msg, isUser: false, html: this.renderMarkdown(msg) }]);
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
