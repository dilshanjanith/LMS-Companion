import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { resolve } from 'node:path';

const dll = resolve(process.argv[2] ?? 'backend/src/LmsCompanion.Mcp.Local/bin/Release/net10.0/publish/LmsCompanion.Mcp.Local.dll');
const child = spawn('dotnet', [dll, process.argv[3] ?? 'serve'], { stdio: ['pipe', 'pipe', 'pipe'] });
const pending = new Map();
let nextId = 1;
let stderr = '';

child.stderr.setEncoding('utf8');
child.stderr.on('data', chunk => { stderr += chunk; });
createInterface({ input: child.stdout }).on('line', line => {
  let message;
  try { message = JSON.parse(line); } catch { return; }
  if (message.id == null) return;
  const waiter = pending.get(message.id);
  if (!waiter) return;
  pending.delete(message.id);
  waiter.resolve(message);
});
child.on('exit', code => {
  for (const waiter of pending.values()) waiter.reject(new Error(`MCP process exited ${code}: ${stderr.trim()}`));
  pending.clear();
});

function request(method, params = {}) {
  const id = nextId++;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`MCP request timed out: ${method}; ${stderr.trim()}`));
    }, 20000);
    pending.set(id, {
      resolve: value => { clearTimeout(timer); resolve(value); },
      reject: error => { clearTimeout(timer); reject(error); }
    });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
}

try {
  const initialized = await request('initialize', {
    protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'local-smoke', version: '1.0' }
  });
  if (initialized.error) throw new Error(`initialize: ${JSON.stringify(initialized.error)}`);
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const tools = await request('tools/list');
  if (tools.error) throw new Error(`tools/list: ${JSON.stringify(tools.error)}`);
  console.log(`Tools: ${(tools.result?.tools ?? []).map(tool => tool.name).join(', ')}`);
  const courses = await request('tools/call', { name: 'get_my_courses', arguments: {} });
  if (courses.error) throw new Error(`tools/call: ${JSON.stringify(courses.error)}`);
  const structured = courses.result?.structuredContent;
  const count = Array.isArray(structured) ? structured.length
    : Array.isArray(structured?.result) ? structured.result.length
    : Array.isArray(structured?.items) ? structured.items.length : 'unknown';
  console.log(`Course tool result: isError=${courses.result?.isError ?? false}, count=${count}`);
  if (courses.result?.isError) {
    console.log(`Safe error: ${courses.result?.content?.filter(item => item.type === 'text').map(item => item.text).join(' ')}`);
    process.exitCode = 1;
  }
  if ((tools.result?.tools ?? []).some(tool => tool.name === 'get_upcoming_canvas_events')) {
    const upcoming = await request('tools/call', { name: 'get_upcoming_canvas_events', arguments: {} });
    if (upcoming.error) throw new Error(`upcoming tools/call: ${JSON.stringify(upcoming.error)}`);
    console.log(`Upcoming tool result: isError=${upcoming.result?.isError ?? false}`);
    if (upcoming.result?.isError) process.exitCode = 1;
  }
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  child.kill();
}
