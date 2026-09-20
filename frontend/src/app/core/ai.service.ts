import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL, joinApiUrl } from './api-config';
import { AuthService } from './auth.service';
import { AgentEvent, ModelList } from './models';

export interface ChatBody {
  message: string;
  provider?: string;
  model?: string;
  conversationId?: string | null;
  viewStart?: string;
  viewEnd?: string;
}

@Injectable({ providedIn: 'root' })
export class AiService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = inject(API_BASE_URL);

  models(): Promise<ModelList> {
    return firstValueFrom(this.http.get<ModelList>('/api/ai/models'));
  }

  deleteConversation(id: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/ai/conversations/${id}`));
  }

  /**
   * POSTs a question and yields the agent's steps as they stream in (server-sent events over fetch, because
   * EventSource cannot POST or send an Authorization header).
   */
  async *chat(body: ChatBody, signal: AbortSignal): AsyncGenerator<AgentEvent> {
    const res = await fetch(joinApiUrl(this.baseUrl, '/api/ai/chat'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${this.auth.token() ?? ''}` },
      body: JSON.stringify(body),
      signal,
    });

    if (!res.ok || !res.body) {
      if (res.status === 401) this.auth.logout();
      const problem = await res.json().catch(() => null);
      throw new Error(res.status === 429 ? 'Too many questions in a short time. Please wait a moment.' : (problem?.detail ?? `Request failed (${res.status}).`));
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      let end: number;
      while ((end = buffer.indexOf('\n\n')) >= 0) {
        const frame = buffer.slice(0, end);
        buffer = buffer.slice(end + 2);
        for (const line of frame.split('\n')) {
          if (line.startsWith('data:')) yield JSON.parse(line.slice(5).trim()) as AgentEvent;
        }
      }
    }
  }
}
