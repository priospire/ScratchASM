const assert = require('node:assert/strict');
const cp = require('node:child_process');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const host = path.join(root, 'ScratchASM.LanguageHost.exe');
const messages = [
  { jsonrpc: '2.0', id: 1, method: 'initialize', params: {} },
  { jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} },
  { jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'analyze_source', arguments: { source: 'stage {\n  @greenflag:\n    unknown.block 1\n}\n' } } }
];
const mcp = cp.spawnSync(host, ['--mcp', '--workspace', root], {
  input: '{\n' + messages.map(message => JSON.stringify(message)).join('\n') + '\n',
  encoding: 'utf8', windowsHide: true, timeout: 60000
});
assert.equal(mcp.status, 0, mcp.stderr || String(mcp.error));
const results = mcp.stdout.trim().split(/\r?\n/).map(line => JSON.parse(line));
assert.equal(results[0].error.code, -32700);
assert.equal(results[0].id, null);
assert.ok(results.find(result => result.id === 2).result.tools.some(tool => tool.name === 'decompile_sb3'));
assert.ok(results.find(result => result.id === 3).result.content[0].text.includes('CTS1008'));

function frame(payload) {
  const bytes = Buffer.from(payload);
  return Buffer.concat([Buffer.from(`Content-Length: ${bytes.length}\r\n\r\n`), bytes]);
}
const lsp = cp.spawnSync(host, ['--lsp'], {
  input: Buffer.concat([frame('{'), frame(JSON.stringify(messages[0]))]),
  windowsHide: true, timeout: 60000
});
assert.equal(lsp.status, 0, String(lsp.stderr || lsp.error));
let remaining = lsp.stdout;
const responses = [];
while (remaining.length) {
  const end = remaining.indexOf('\r\n\r\n');
  assert.ok(end >= 0);
  const length = Number(/Content-Length:\s*(\d+)/i.exec(remaining.subarray(0, end).toString())[1]);
  responses.push(JSON.parse(remaining.subarray(end + 4, end + 4 + length).toString()));
  remaining = remaining.subarray(end + 4 + length);
}
assert.equal(responses[0].error.code, -32700);
assert.ok(responses[1].result.capabilities.completionProvider);
console.log('PASS: packaged MCP and LSP hosts initialize, return diagnostics, and recover after malformed JSON.');
