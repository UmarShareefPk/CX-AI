import { ChangeDetectionStrategy, Component, ElementRef, effect, inject, model, signal, untracked, viewChild } from '@angular/core';
import { marked } from 'marked';
import { AiService } from '../core/ai.service';
import { AuthService } from '../core/auth.service';
import { FilterService } from '../core/filter.service';
import { AgentEvent, ModelInfo } from '../core/models';

interface Step {
  callId: string;
  name: string;
  args: string;
  result?: string;
  isError?: boolean;
}

interface Message {
  role: 'user' | 'assistant';
  text: string;
  steps: Step[];
  status?: 'streaming' | 'done' | 'error' | 'stopped';
  error?: string;
  meta?: string;
}

const MODEL_KEY = 'cx.ai.model';

const TOOL_LABELS: Record<string, string> = {
  get_dealer_score: 'Looked up the score',
  get_dealer_rank: 'Looked up the rank',
  get_top_ranked_score: 'Looked up the top score',
  get_leaderboard: 'Loaded the leaderboard',
  get_score_trend: 'Loaded the score trend',
  search_policy: 'Searched the policy documents',
};

const DEALER_SUGGESTIONS = [
  'What is my score this month?',
  'What is my score this quarter?',
  'What is my rank?',
  'How can I improve my score?',
  'What is the score of the highest rank in the current month?',
];

const ADMIN_SUGGESTIONS = [
  'Which dealer is ranked first this month, and what is their score?',
  'What is the network score this quarter?',
  'How is the dealer score calculated?',
  'Which dealers are in the bottom three this month?',
];

@Component({
  selector: 'cx-ai-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'open() && open.set(false)' },
  template: `
    @if (open()) {
      <aside id="ai-panel" class="panel" role="dialog" aria-label="Ask AI">
        <header>
          <h2><span aria-hidden="true">✦</span> Ask AI</h2>
          <div class="actions">
            <button class="btn btn-sm" type="button" (click)="newChat()" [disabled]="busy() || !messages().length">New chat</button>
            <button class="btn btn-sm btn-ghost" type="button" (click)="open.set(false)" aria-label="Close AI panel">✕</button>
          </div>
        </header>

        <label class="field picker">
          Model
          <select [value]="selected()" (change)="onModel($any($event.target).value)" [disabled]="busy()">
            @for (m of models(); track m.id) {
              <option [value]="m.id" [selected]="m.id === selected()" [disabled]="!m.supportsTools">
                {{ m.displayName }}{{ m.supportsTools ? '' : ' — no tool support' }}
              </option>
            }
          </select>
        </label>
        @if (modelError()) {
          <p class="notice error" role="alert">{{ modelError() }}</p>
        }

        <div #scroller class="messages" aria-live="polite">
          @if (!messages().length) {
            <div class="intro">
              <p>Ask about your scores, rank, or how to improve. Answers come from your live survey data and the policy documents.</p>
              <div class="suggestions">
                @for (s of suggestions(); track s) {
                  <button class="btn btn-sm" type="button" (click)="send(s)" [disabled]="busy() || !selected()">{{ s }}</button>
                }
              </div>
            </div>
          }

          @for (m of messages(); track $index) {
            @if (m.role === 'user') {
              <div class="bubble user">{{ m.text }}</div>
            } @else {
              <div class="assistant">
                @for (s of m.steps; track s.callId) {
                  <details class="step" [class.err]="s.isError">
                    <summary>
                      <span class="dot" [class.run]="s.result === undefined" aria-hidden="true"></span>
                      {{ label(s.name) }}
                      @if (s.result === undefined) { <span class="muted">…</span> }
                      @else if (s.isError) { <span class="tone-bad chip">refused</span> }
                    </summary>
                    <p class="muted"><code>{{ s.name }}</code> {{ s.args }}</p>
                    @if (s.result !== undefined) { <pre>{{ s.result }}</pre> }
                  </details>
                }
                @if (m.status === 'streaming' && !m.text) {
                  <p class="muted thinking" role="status">Thinking…</p>
                }
                @if (m.text) {
                  <div class="bubble md" [innerHTML]="render(m.text)"></div>
                }
                @if (m.status === 'error') {
                  <p class="notice error" role="alert">{{ m.error }}</p>
                }
                @if (m.status === 'stopped') {
                  <p class="muted">Stopped.</p>
                }
                @if (m.meta) {
                  <p class="meta muted">{{ m.meta }}</p>
                }
              </div>
            }
          }
        </div>

        <form class="composer" (submit)="onSubmit($event)">
          <textarea #box rows="2" placeholder="Ask a question…" aria-label="Your question" maxlength="2000"
                    [value]="draft()" (input)="draft.set($any($event.target).value)"
                    (keydown.enter)="onEnter($event)"></textarea>
          @if (busy()) {
            <button class="btn" type="button" (click)="stop()">Stop</button>
          } @else {
            <button class="btn btn-primary" type="submit" [disabled]="!draft().trim() || !selected()">Send</button>
          }
        </form>
      </aside>
    }
  `,
  styles: `
    .panel {
      position: fixed; z-index: 20; top: 0; right: 0; bottom: 0; width: min(480px, 100vw);
      background: var(--surface); border-left: 1px solid var(--border); box-shadow: -8px 0 24px rgba(0, 0, 0, 0.12);
      display: flex; flex-direction: column; padding: 16px; gap: 12px;
    }
    header { display: flex; align-items: center; justify-content: space-between; }
    .actions { display: flex; gap: 6px; }
    .messages { flex: 1; overflow-y: auto; display: flex; flex-direction: column; gap: 12px; padding-right: 4px; }
    .intro { display: grid; gap: 12px; color: var(--muted); }
    .suggestions { display: flex; flex-wrap: wrap; gap: 8px; }
    .suggestions .btn { text-align: left; height: auto; padding: 8px 12px; font-weight: 500; }
    .bubble { padding: 10px 14px; border-radius: 14px; max-width: 100%; overflow-wrap: anywhere; }
    .bubble.user { align-self: flex-end; background: var(--brand); color: var(--brand-ink); border-bottom-right-radius: 4px; max-width: 88%; }
    .assistant { display: grid; gap: 8px; }
    .assistant .bubble { background: var(--surface-2); border-bottom-left-radius: 4px; }
    .step { border: 1px solid var(--border); border-radius: 10px; padding: 6px 10px; font-size: 0.85rem; }
    .step summary { cursor: pointer; display: flex; align-items: center; gap: 8px; }
    .step.err { border-color: var(--bad); }
    .step pre { margin: 6px 0 0; max-height: 180px; overflow: auto; white-space: pre-wrap; word-break: break-word; font-size: 0.78rem; background: var(--surface-2); padding: 8px; border-radius: 8px; }
    .step p { margin-top: 6px; overflow-wrap: anywhere; }
    .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--ok); flex: none; }
    .dot.run { background: var(--warn); animation: pulse 1s infinite; }
    @keyframes pulse { 50% { opacity: 0.3; } }
    .meta { font-size: 0.75rem; }
    .composer { display: flex; gap: 8px; align-items: flex-end; }
    .composer textarea { flex: 1; }
  `,
})
export class AiPanel {
  private readonly ai = inject(AiService);
  private readonly auth = inject(AuthService);
  private readonly filter = inject(FilterService);

  readonly open = model(false);

  protected readonly models = signal<ModelInfo[]>([]);
  protected readonly selected = signal('');
  protected readonly modelError = signal<string | null>(null);
  protected readonly messages = signal<Message[]>([]);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly box = viewChild<ElementRef<HTMLTextAreaElement>>('box');
  private conversationId: string | null = null;
  private conversationProvider: string | null = null;
  private abort?: AbortController;
  private modelsLoaded = false;

  protected suggestions = () => (this.auth.isAdmin() ? ADMIN_SUGGESTIONS : DEALER_SUGGESTIONS);

  constructor() {
    effect(() => {
      if (!this.open()) return;
      untracked(() => {
        void this.loadModels();
        setTimeout(() => this.box()?.nativeElement.focus());
      });
    });
    // A different user must not see the previous user's chat.
    effect(() => {
      this.auth.user();
      untracked(() => this.reset());
    });
  }

  protected label = (tool: string) => TOOL_LABELS[tool] ?? tool;

  protected render(markdown: string): string {
    return marked.parse(markdown, { async: false, gfm: true, breaks: true }) as string; // Angular sanitizes [innerHTML]
  }

  protected onModel(id: string): void {
    const chosen = this.models().find((m) => m.id === id);
    if (!chosen) return;
    // Hidden reasoning state is provider-specific, so switching provider starts a fresh conversation.
    if (this.conversationProvider && this.conversationProvider !== chosen.provider) this.reset();
    this.selected.set(id);
    try {
      localStorage.setItem(MODEL_KEY, id);
    } catch {
      /* preference not persisted */
    }
  }

  protected newChat(): void {
    this.stop();
    this.reset();
  }

  protected stop(): void {
    this.abort?.abort();
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    void this.send(this.draft());
  }

  protected onEnter(event: Event): void {
    if ((event as KeyboardEvent).shiftKey) return; // Shift+Enter inserts a newline
    event.preventDefault();
    void this.send(this.draft());
  }

  protected async send(text: string): Promise<void> {
    const question = text.trim();
    const chosen = this.models().find((m) => m.id === this.selected());
    if (!question || this.busy() || !chosen) return;

    this.draft.set('');
    this.busy.set(true);
    this.messages.update((m) => [...m, { role: 'user', text: question, steps: [] }, { role: 'assistant', text: '', steps: [], status: 'streaming' }]);
    this.scrollSoon();

    const hadConversation = this.conversationId !== null;
    const controller = (this.abort = new AbortController());
    const range = this.filter.range();
    try {
      const stream = this.ai.chat(
        { message: question, provider: chosen.provider, model: chosen.model, conversationId: this.conversationId, viewStart: range.start, viewEnd: range.end },
        controller.signal,
      );
      for await (const event of stream) this.apply(event);
      this.patchLast((m) => (m.status === 'streaming' ? { ...m, status: 'done' } : m));
    } catch (e) {
      const aborted = controller.signal.aborted;
      this.patchLast((m) => ({ ...m, status: aborted ? 'stopped' : 'error', error: aborted ? undefined : (e as Error).message }));
    } finally {
      // A turn that never completed was not saved, so a brand new conversation id is not valid on the server.
      if (!hadConversation && this.lastStatus() !== 'done') this.conversationId = null;
      this.busy.set(false);
      this.scrollSoon();
    }
  }

  private apply(e: AgentEvent): void {
    switch (e.type) {
      case 'conversation':
        this.conversationId = e.conversationId ?? this.conversationId;
        this.conversationProvider = e.provider ?? this.conversationProvider;
        break;
      case 'text':
        this.patchLast((m) => ({ ...m, text: m.text + (e.text ?? '') }));
        break;
      case 'tool_call':
        this.patchLast((m) => ({ ...m, steps: [...m.steps, { callId: e.callId ?? '', name: e.toolName ?? '', args: e.arguments ?? '' }] }));
        break;
      case 'tool_result':
        this.patchLast((m) => ({
          ...m,
          steps: m.steps.map((s) => (s.callId === e.callId ? { ...s, result: e.result ?? '', isError: e.isError } : s)),
        }));
        break;
      case 'done':
        this.patchLast((m) => ({ ...m, status: 'done', meta: this.meta(e) }));
        break;
      case 'error':
        if (e.text === 'Conversation not found.') this.conversationId = null;
        this.patchLast((m) => ({ ...m, status: 'error', error: e.text ?? 'Something went wrong.' }));
        break;
    }
    this.scrollSoon();
  }

  private meta(e: AgentEvent): string {
    const seconds = e.elapsedMs ? `${(e.elapsedMs / 1000).toFixed(1)}s` : '';
    const tokens = e.inputTokens || e.outputTokens ? `${e.inputTokens ?? 0} in / ${e.outputTokens ?? 0} out tokens` : '';
    return [this.models().find((m) => m.id === this.selected())?.model, seconds, tokens].filter(Boolean).join(' · ');
  }

  private patchLast(change: (m: Message) => Message): void {
    this.messages.update((list) => (list.length ? [...list.slice(0, -1), change(list[list.length - 1])] : list));
  }

  private lastStatus = () => this.messages().at(-1)?.status;

  private reset(): void {
    this.messages.set([]);
    this.conversationId = null;
    this.conversationProvider = null;
  }

  private scrollSoon(): void {
    requestAnimationFrame(() => {
      const el = this.scroller()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }

  private async loadModels(): Promise<void> {
    if (this.modelsLoaded) return;
    try {
      const list = await this.ai.models();
      this.models.set(list.models);
      this.modelsLoaded = true;

      let remembered: string | null = null;
      try {
        remembered = localStorage.getItem(MODEL_KEY);
      } catch {
        /* no stored preference */
      }
      const usable = (id: string | null) => list.models.find((m) => m.id === id && m.supportsTools)?.id;
      const server = list.models.find((m) => m.isDefault && m.supportsTools)?.id;
      this.selected.set(usable(remembered) ?? server ?? list.models.find((m) => m.supportsTools)?.id ?? '');
      this.modelError.set(list.models.some((m) => m.supportsTools) ? null : 'No AI model is available. Start Ollama (with a tool-capable model) or configure a hosted provider.');
    } catch {
      this.modelError.set('The list of AI models could not be loaded.');
    }
  }
}
