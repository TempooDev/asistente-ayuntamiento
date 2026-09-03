import { Component, OnInit, OnDestroy, AfterViewInit, inject, ViewChild, ElementRef, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ChatService, ChatSessionSummaryDto } from '../../services/chat/chat.service';
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
export class ChatPanelComponent implements OnInit, OnDestroy, AfterViewInit {
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
  @ViewChild('chatInput') private chatInput!: ElementRef<HTMLTextAreaElement>;

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

  ngAfterViewInit() {
    this.focusInput();
  }

  focusInput() {
    setTimeout(() => {
      if (this.chatInput && this.chatInput.nativeElement) {
        this.chatInput.nativeElement.focus();
      }
    }, 100);
  }

  ngOnDestroy() {
    // Cleanup if needed
  }

  async loadSessions() {
    const data = await this.chatService.getSessions();
    this.sessions.set(data);
  }

  chatToDelete = signal<string | null>(null);

  confirmDeleteSession(id: string) {
    this.chatToDelete.set(id);
  }

  cancelDeleteSession() {
    this.chatToDelete.set(null);
  }

  async executeDeleteSession() {
    const id = this.chatToDelete();
    if (!id) return;
    
    this.chatToDelete.set(null);
    
    try {
      await this.chatService.deleteSession(id);
      
      // Update local state
      this.chatService.sessionMessages.delete(id);
      
      // If we deleted the current active session, create a new one
      if (this.currentSessionId() === id) {
        await this.createNewChat();
      } else {
        await this.loadSessions();
      }
    } catch (e) {
      console.error('Error al eliminar chat:', e);
    }
  }

  async selectSession(id: string) {
    this.currentSessionId.set(id);
    this.sidebarOpen.set(false);

    // If we have an active stream for this session, restore the generating state
    this.isGenerating.set(this.chatService.activeStreams.has(id));

    if (this.chatService.sessionMessages.has(id)) {
      // Restore from cache so active streams aren't interrupted visually
      this.messages.set([...this.chatService.sessionMessages.get(id)!]);
      this.scrollToBottom();
      this.focusInput();
      return;
    }

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
      this.chatService.sessionMessages.set(id, mapped);
      this.messages.set([...mapped]);
      this.scrollToBottom();
    } catch (e) {
      console.error(e);
    } finally {
      this.isLoadingHistory.set(false);
      this.focusInput();
    }
  }

  async createNewChat() {
    this.sidebarOpen.set(false);
    this.messages.set([]);
    this.currentMessage = '';
    
    try {
      const newId = await this.chatService.createNewSession();
      this.currentSessionId.set(newId);
      this.chatService.sessionMessages.set(newId, []);
      await this.loadSessions();
      this.focusInput();
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

  async sendMessage() {
    if (!this.currentMessage.trim() || !this.isConnected() || this.isGenerating()) return;

    const text = this.currentMessage;
    this.currentMessage = '';
    
    if (!this.currentSessionId()) {
      await this.createNewChat();
    }
    
    const sessionId = this.currentSessionId();
    this.isWaitingForResponse.set(true);
    this.isGenerating.set(true);

    const assistantMsg: ChatMessage = { text: '', isUser: false, html: '' };
    
    // Add to cache
    const currentMsgs = this.chatService.sessionMessages.get(sessionId) || [];
    currentMsgs.push({ text, isUser: true }, assistantMsg);
    this.chatService.sessionMessages.set(sessionId, currentMsgs);

    // Update UI if we're still on this chat
    if (this.currentSessionId() === sessionId) {
      this.messages.set([...currentMsgs]);
      this.scrollToBottom();
    }

    try {
      const stream = this.chatService.streamMessage(sessionId, text);
      let firstChunk = true;

      const sub = stream.subscribe({
        next: (chunk) => {
          if (firstChunk) {
            if (this.currentSessionId() === sessionId) this.isWaitingForResponse.set(false);
            firstChunk = false;
          }
          
          // Modify the object reference in memory (this updates the cache automatically)
          assistantMsg.text += chunk;
          assistantMsg.html = this.renderMarkdown(assistantMsg.text);
          
          // Only trigger UI change detection if the user is currently viewing THIS chat
          if (this.currentSessionId() === sessionId) {
            this.messages.update(msgs => [...msgs]);
            this.scrollToBottom();
          }
        },
        complete: () => {
          this.chatService.activeStreams.delete(sessionId);
          if (this.currentSessionId() === sessionId) {
            if (firstChunk) this.isWaitingForResponse.set(false);
            this.isGenerating.set(false);
            this.focusInput();
          }
          this.loadSessions();
        },
        error: (err) => {
          assistantMsg.text += `
[Error: ${err.message || err}]`;
          assistantMsg.html = this.renderMarkdown(assistantMsg.text);
          if (this.currentSessionId() === sessionId) this.messages.update(msgs => [...msgs]);
          console.error(err);
          this.chatService.activeStreams.delete(sessionId);
          if (this.currentSessionId() === sessionId) {
            this.isWaitingForResponse.set(false);
            this.isGenerating.set(false);
            this.focusInput();
          }
        }
      });
      
      this.chatService.activeStreams.set(sessionId, sub);
    } catch (e: any) {
      assistantMsg.text += `\n[Error: ${e.message || e}]`;
      assistantMsg.html = this.renderMarkdown(assistantMsg.text);
      if (this.currentSessionId() === sessionId) this.messages.update(msgs => [...msgs]);
      console.error(e);
      this.isWaitingForResponse.set(false);
      this.isGenerating.set(false);
      this.focusInput();
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
