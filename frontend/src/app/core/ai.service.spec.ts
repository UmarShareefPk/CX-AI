import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { AiService } from './ai.service';
import { AuthService } from './auth.service';
import { AgentEvent } from './models';

/** Builds a fetch Response whose body arrives in the given chunks, like a real event stream. */
function streamOf(chunks: string[], status = 200): Response {
  const encoder = new TextEncoder();
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      chunks.forEach((c) => controller.enqueue(encoder.encode(c)));
      controller.close();
    },
  });
  return new Response(body, { status, headers: { 'Content-Type': 'text/event-stream' } });
}

async function collect(gen: AsyncGenerator<AgentEvent>): Promise<AgentEvent[]> {
  const events: AgentEvent[] = [];
  for await (const e of gen) events.push(e);
  return events;
}

describe('AiService.chat (server-sent events over fetch)', () => {
  let service: AiService;
  const logout = vi.fn();

  beforeEach(() => {
    logout.mockReset();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), { provide: AuthService, useValue: { token: () => 'jwt', logout } }],
    });
    service = TestBed.inject(AiService);
  });

  afterEach(() => vi.unstubAllGlobals());

  it('parses events even when a frame is split across network chunks', async () => {
    const frame1 = 'data: {"type":"text","text":"Hel';
    const frame2 = 'lo"}\n\ndata: {"type":"done","elapsedMs":5}\n\n';
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(streamOf([frame1, frame2])));

    const events = await collect(service.chat({ message: 'hi' }, new AbortController().signal));

    expect(events.map((e) => e.type)).toEqual(['text', 'done']);
    expect(events[0].text).toBe('Hello');
  });

  it('sends the bearer token and the message', async () => {
    const fetchMock = vi.fn().mockResolvedValue(streamOf([]));
    vi.stubGlobal('fetch', fetchMock);

    await collect(service.chat({ message: 'What is my rank?', provider: 'ollama', model: 'gemma4:e4b' }, new AbortController().signal));

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/ai/chat');
    expect(init.headers.Authorization).toBe('Bearer jwt');
    expect(JSON.parse(init.body).message).toBe('What is my rank?');
  });

  it('surfaces the server problem detail for a rejected request', async () => {
    const problem = new Response(JSON.stringify({ detail: 'The message must be between 1 and 2000 characters.' }), { status: 400 });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem));

    await expect(collect(service.chat({ message: '' }, new AbortController().signal))).rejects.toThrow(/between 1 and 2000/);
  });

  it('signs the user out when the token has expired', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 401 })));

    await expect(collect(service.chat({ message: 'hi' }, new AbortController().signal))).rejects.toThrow();
    expect(logout).toHaveBeenCalled();
  });

  it('explains rate limiting in plain language', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 429 })));

    await expect(collect(service.chat({ message: 'hi' }, new AbortController().signal))).rejects.toThrow(/Too many/);
  });
});
