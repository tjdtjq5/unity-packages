#!/usr/bin/env node
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { PipeServer } from './pipe-server.js';
import { McpChannel } from './mcp-channel.js';

// ── 설정 ──
const PIPE_HASH = process.env.CLAUDE_UNITY_PIPE_HASH || 'default';
const PIPE_NAME = process.platform === 'win32'
  ? `\\\\.\\pipe\\claude-unity-${PIPE_HASH}`
  : `/tmp/claude-unity-${PIPE_HASH}.sock`;

console.error(`[bridge] 시작 — pipe: ${PIPE_NAME}`);

// ── Discord 상태 (Phase 2에서 확장) ──
let discordClient = null;
let eventRouter = null;

// ── MCP Channel 생성 ──
const mcpChannel = new McpChannel({
  onToolCall: async (name, args) => {
    switch (name) {
      case 'reply': {
        // Phase 2: Discord reply
        if (eventRouter) {
          eventRouter.handleClaudeReply(args.text);
          return '메시지 전송됨';
        }
        return 'Discord 미연결 — 메시지를 보낼 수 없습니다';
      }

      case 'send_notification': {
        // Phase 2: Discord notification
        if (eventRouter) {
          eventRouter.handleClaudeNotification(args.text, args.category || 'info');
          return '알림 전송됨';
        }
        return 'Discord 미연결';
      }

      case 'set_cooldown': {
        const seconds = args.seconds || 30;
        pipeServer.sendToUnity({
          type: 'set_cooldown',
          sourceFile: args.sourceFile,
          seconds,
        });
        return `${args.sourceFile} 쿨다운 ${seconds}초 설정됨`;
      }

      default:
        throw new Error(`알 수 없는 도구: ${name}`);
    }
  },
});

// ── Named Pipe 서버 ──
const pipeServer = new PipeServer(PIPE_NAME);

pipeServer.on('connect', () => {
  console.error('[bridge] Unity 연결됨');
});

pipeServer.on('disconnect', () => {
  console.error('[bridge] Unity 연결 끊김');
});

pipeServer.on('message', async (msg) => {
  try {
    switch (msg.type) {
      case 'unity_event':
        // Unity 이벤트 → Claude에 전달
        if (eventRouter) {
          eventRouter.handleUnityEvent(msg);
        } else {
          await mcpChannel.sendToClaudeFromUnity(msg);
        }
        break;

      case 'config':
        // Discord 설정 수신 (Phase 2에서 구현)
        console.error('[bridge] 설정 수신:', msg.discordMode || 'no-discord');
        await handleConfig(msg);
        break;

      default:
        console.error('[bridge] 알 수 없는 메시지 타입:', msg.type);
    }
  } catch (err) {
    console.error('[bridge] 메시지 처리 에러:', err.message);
  }
});

// ── Discord 설정 처리 (Phase 2 스텁) ──
async function handleConfig(config) {
  // Phase 2에서 discord-client.js + event-router.js 연동
  // 현재는 상태만 Unity에 전달
  pipeServer.sendToUnity({
    type: 'bridge_status',
    status: 'connected',
    message: 'Bridge 연결됨 (Discord: Phase 2)',
  });
}

// ── 시작 ──
async function main() {
  // [H1] MCP 먼저 연결 — Pipe 이벤트가 오기 전에 transport 준비
  const transport = new StdioServerTransport();
  await mcpChannel.server.connect(transport);
  console.error('[bridge] MCP Channel 준비 완료');

  // Named Pipe 서버 시작 (이제 이벤트 수신해도 안전)
  await pipeServer.start();
}

main().catch((err) => {
  console.error('[bridge] 치명적 에러:', err);
  process.exit(1);
});

// ── 종료 처리 ──
process.on('SIGINT', () => {
  console.error('[bridge] 종료 중...');
  pipeServer.stop();
  process.exit(0);
});

process.on('SIGTERM', () => {
  pipeServer.stop();
  process.exit(0);
});
