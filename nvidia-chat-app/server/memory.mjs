import fs from 'node:fs/promises';
import path from 'node:path';

function normalize(text) {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function uniqueTokens(query) {
  const tokens = normalize(query)
    .split(' ')
    .map((t) => t.trim())
    .filter((t) => t.length >= 2);
  return Array.from(new Set(tokens));
}

function chunkText(text, chunkSize = 1200, overlap = 200) {
  const cleaned = String(text)
    .replace(/\r\n/g, '\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
  if (!cleaned) return [];

  const parts = cleaned.split(/\n\n+/g);
  const chunks = [];

  let current = '';
  for (const part of parts) {
    const candidate = current ? `${current}\n\n${part}` : part;
    if (candidate.length <= chunkSize) {
      current = candidate;
      continue;
    }

    if (current) chunks.push(current);

    if (part.length > chunkSize) {
      let i = 0;
      while (i < part.length) {
        const end = Math.min(i + chunkSize, part.length);
        chunks.push(part.slice(i, end));
        i = end - overlap;
        if (i < 0) i = 0;
        if (end === part.length) break;
      }
      current = '';
    } else {
      current = part;
    }
  }
  if (current) chunks.push(current);

  return chunks;
}

function countOccurrences(haystack, needle) {
  if (!needle) return 0;
  let count = 0;
  let idx = 0;
  while (true) {
    idx = haystack.indexOf(needle, idx);
    if (idx === -1) break;
    count++;
    idx += needle.length;
  }
  return count;
}

async function listFilesRecursive(dir) {
  const entries = await fs.readdir(dir, { withFileTypes: true });
  const out = [];
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) {
      out.push(...(await listFilesRecursive(full)));
    } else if (e.isFile()) {
      out.push(full);
    }
  }
  return out;
}

async function loadMemoryChunks(memoryDir) {
  let files = [];
  try {
    files = await listFilesRecursive(memoryDir);
  } catch {
    return [];
  }

  const allowed = new Set(['.md', '.txt', '.json']);
  const chunks = [];
  for (const file of files) {
    const ext = path.extname(file).toLowerCase();
    if (!allowed.has(ext)) continue;

    let raw = '';
    try {
      raw = await fs.readFile(file, 'utf8');
    } catch {
      continue;
    }

    const rel = path.relative(process.cwd(), file).replace(/\\/g, '/');
    for (const c of chunkText(raw)) {
      chunks.push({ source: rel, text: c, haystack: normalize(c) });
    }
  }

  return chunks;
}

let cached = null;

export async function initMemoryIndex(memoryDir) {
  cached = await loadMemoryChunks(memoryDir);
  return cached;
}

export async function getMemoryContext(query, opts = {}) {
  const maxSnippets = Math.max(1, opts.maxSnippets ?? 4);
  const maxChars = Math.max(500, opts.maxChars ?? 3500);
  const tokens = uniqueTokens(query);
  if (tokens.length === 0) return '';
  if (!cached || cached.length === 0) return '';

  const scored = cached
    .map((c) => {
      let score = 0;
      for (const t of tokens) score += countOccurrences(c.haystack, t);
      return { chunk: c, score };
    })
    .filter((x) => x.score > 0)
    .sort((a, b) => b.score - a.score);

  if (scored.length === 0) return '';

  let out = '';
  let used = 0;
  for (const { chunk } of scored.slice(0, maxSnippets)) {
    const block = `[${chunk.source}]\n${chunk.text.trim()}\n\n`;
    if (used + block.length > maxChars) break;
    out += block;
    used += block.length;
  }

  return out.trim();
}
