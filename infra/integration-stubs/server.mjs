import { appendFileSync, existsSync, readFileSync } from 'node:fs';
import { createServer } from 'node:http';

const postFile = '/data/telegram.jsonl';
const posts = existsSync(postFile)
  ? readFileSync(postFile, 'utf8').trim().split('\n').filter(Boolean).map(line => JSON.parse(line))
  : [];

function reply(response, status, body) {
  response.writeHead(status, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(body));
}

async function readJson(request) {
  let body = '';
  request.setEncoding('utf8');
  for await (const chunk of request) {
    body += chunk;
    if (body.length > 65536) throw new SyntaxError('Request is too large');
  }
  return JSON.parse(body);
}

async function handle(request, response) {
  if (request.method === 'GET' && request.url === '/health') return reply(response, 200, { ok: true });
  if (request.method === 'GET' && request.url === '/posts') return reply(response, 200, posts);

  if (request.method === 'POST' && request.url === '/bot1234:local_stub/sendMessage') {
    const { chat_id, text } = await readJson(request);
    if (typeof chat_id !== 'string' || !/^@[A-Za-z0-9_]{5,32}$/.test(chat_id) || typeof text !== 'string' || !text.trim()) {
      return reply(response, 400, { ok: false, description: 'Expected a public channel and message text' });
    }
    const post = { message_id: posts.length + 1, chat: { username: chat_id.slice(1), type: 'channel' }, text };
    appendFileSync(postFile, `${JSON.stringify(post)}\n`);
    posts.push(post);
    return reply(response, 200, { ok: true, result: post });
  }

  if (request.method === 'POST' && request.url === '/v1/chat/completions' && request.headers.authorization === 'Bearer local-stub') {
    const { model, messages } = await readJson(request);
    const source = Array.isArray(messages) && messages.find(message => message?.role === 'user')?.content;
    const { title, summary } = typeof source === 'string' ? JSON.parse(source) : {};
    if (typeof model !== 'string' || typeof title !== 'string' || typeof summary !== 'string') {
      return reply(response, 400, { error: { message: 'Expected an article title and summary' } });
    }
    const content = JSON.stringify({ text: Array.from(`${title}\n\n${summary}`.trim()).slice(0, 550).join('') });
    return reply(response, 200, {
      id: 'chatcmpl-local-stub', object: 'chat.completion', created: Math.floor(Date.now() / 1000), model,
      choices: [{ index: 0, message: { role: 'assistant', content }, finish_reason: 'stop' }],
    });
  }

  return reply(response, 404, { error: { message: 'Unknown stub endpoint' } });
}

createServer((request, response) => {
  handle(request, response).catch(error => {
    const invalidInput = error instanceof SyntaxError || error instanceof TypeError;
    reply(response, invalidInput ? 400 : 500, { error: { message: invalidInput ? 'Invalid JSON request' : 'Stub request failed' } });
  });
}).listen(8080, '0.0.0.0');
