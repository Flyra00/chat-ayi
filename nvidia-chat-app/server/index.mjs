import path from 'node:path';
import fs from 'node:fs/promises';
import express from 'express';
import dotenv from 'dotenv';
import { initMemoryIndex, getMemoryContext } from './memory.mjs';

dotenv.config({ path: path.resolve(process.cwd(), 'server/.env') });

const PORT = Number(process.env.PORT || 3001);
const UPSTREAM_API_KEY = process.env.CEREBRAS_API_KEY || process.env.NVIDIA_API_KEY || '';
const UPSTREAM_API_URL =
  process.env.CEREBRAS_API_URL ||
  process.env.NVIDIA_API_URL ||
  'https://api.cerebras.ai/v1/chat/completions';
const MEMORY_DIR = path.resolve(process.cwd(), process.env.MEMORY_DIR || 'memory');

if (!UPSTREAM_API_KEY) {
  // eslint-disable-next-line no-console
  console.warn('[server] Missing CEREBRAS_API_KEY (set in server/.env or environment)');
}

await initMemoryIndex(MEMORY_DIR);

const app = express();
app.disable('x-powered-by');
app.use(express.json({ limit: '1mb' }));

app.post('/api/chat/completions', async (req, res) => {
  const controller = new AbortController();
  req.on('close', () => controller.abort());

  try {
    const model = typeof req.body?.model === 'string' ? req.body.model : '';
    const inputMessages = Array.isArray(req.body?.messages) ? req.body.messages : [];

    const messages = inputMessages
      .filter((m) => m && (m.role === 'user' || m.role === 'assistant'))
      .map((m) => ({ role: m.role, content: String(m.content ?? '') }));

    const lastUser = [...messages].reverse().find((m) => m.role === 'user' && m.content);
    const memoryContext = lastUser ? await getMemoryContext(lastUser.content) : '';

    const finalMessages = [];
    if (memoryContext) {
      finalMessages.push({
        role: 'system',
        content:
          'Use the following knowledge base excerpts when helpful. ' +
          'If they are not relevant, ignore them.\n\n' +
          memoryContext
      });
    }
    finalMessages.push(...messages);

    const upstream = await fetch(UPSTREAM_API_URL, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${UPSTREAM_API_KEY}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        model,
        messages: finalMessages,
        temperature: 0.7,
        top_p: 0.9,
        max_tokens: 4096,
        stream: true
      }),
      signal: controller.signal
    });

    if (!upstream.ok) {
      const text = await upstream.text().catch(() => '');
      res.status(upstream.status).json({ error: { message: text || 'Upstream error' } });
      return;
    }

    res.status(200);
    res.setHeader('Content-Type', upstream.headers.get('content-type') || 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache, no-transform');
    res.setHeader('Connection', 'keep-alive');
    res.flushHeaders();

    if (!upstream.body) {
      res.end();
      return;
    }

    const reader = upstream.body.getReader();
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      if (value) res.write(Buffer.from(value));
    }
    res.end();
  } catch (err) {
    if (controller.signal.aborted) return;
    const message = err instanceof Error ? err.message : 'Server error';
    res.status(500).json({ error: { message } });
  }
});

async function tryEnableStatic() {
  const distDir = path.resolve(process.cwd(), 'dist');
  try {
    await fs.access(path.join(distDir, 'index.html'));
  } catch {
    return;
  }

  app.use(express.static(distDir));
  app.get(/.*/, (_req, res) => {
    res.sendFile(path.join(distDir, 'index.html'));
  });
}

await tryEnableStatic();

app.listen(PORT, () => {
  // eslint-disable-next-line no-console
  console.log(`[server] listening on http://localhost:${PORT}`);
});
