#!/usr/bin/env node
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { PipeServer } from './pipe-server.js';
import { McpChannel } from './mcp-channel.js';
import { DiscordClient } from './discord-client.js';
import { EventRouter } from './event-router.js';

// ── 설정 ──
const PIPE_HASH = process.env.CLAUDE_UNITY_PIPE_HASH || 'default';
// .NET의 NamedPipeClientStream은 macOS/Linux에서 자동으로
// /tmp/CoreFxPipe_{name} Unix Domain Socket으로 매핑된다.
// 양쪽 경로가 정확히 일치해야 Unity ↔ Bridge 연결이 성립한다.
// TODO: CoreFxPipe_ 프리픽스는 .NET 런타임 내부 구현 세부사항.
//       향후 변경 시 이 한 줄과 함께 명시적 UDS 프로토콜로 이전 고려.
const PIPE_NAME = process.platform === 'win32'
  ? `\\\\.\\pipe\\claude-unity-${PIPE_HASH}`
  : `/tmp/CoreFxPipe_claude-unity-${PIPE_HASH}`;

console.error(`[bridge] 시작 — pipe: ${PIPE_NAME}`);

// ── Discord 상태 ──
let discordClient = null;
let eventRouter = null;

// ── MCP Channel 생성 ──
const mcpChannel = new McpChannel({
  onToolCall: async (name, args) => {
    switch (name) {
      case 'reply': {
        if (eventRouter) {
          await eventRouter.handleClaudeReply(args.text);
          return '메시지 전송됨';
        }
        return 'Discord 미연결 — 메시지를 보낼 수 없습니다';
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
        if (eventRouter) {
          await eventRouter.handleUnityEvent(msg);
        } else {
          await mcpChannel.sendToClaudeFromUnity(msg);
        }
        break;

      case 'config':
        console.error('[bridge] 설정 수신:', msg.discordEnabled ? 'enabled' : 'disabled');
        await handleConfig(msg);
        break;

      default:
        console.error('[bridge] 알 수 없는 메시지 타입:', msg.type);
    }
  } catch (err) {
    console.error('[bridge] 메시지 처리 에러:', err.message);
  }
});

// ── Discord 설정 처리 ──
async function handleConfig(config) {
  // Discord 비활성 또는 토큰 없음
  if (!config.discordEnabled || !config.discordBotToken) {
    if (discordClient) {
      await discordClient.disconnect();
      discordClient = null;
      eventRouter = null;
      console.error('[bridge] Discord 연결 해제');
    }
    pipeServer.sendToUnity({
      type: 'bridge_status',
      status: 'connected',
      message: 'Discord OFF',
    });
    return;
  }

  // Discord (재)연결
  if (discordClient) {
    await discordClient.disconnect();
    discordClient = null;
    eventRouter = null;
  }

  discordClient = new DiscordClient({
    token: config.discordBotToken,
    channelId: config.discordChannelId,
    allowedUsers: [],
  });

  eventRouter = new EventRouter({ mcpChannel, discordClient, pipeServer });

  discordClient.on('message', (msg) => eventRouter.handleDiscordMessage(msg));
  discordClient.on('command', (cmd) => eventRouter.handleDiscordCommand(cmd));

  discordClient.on('connected', () => {
    console.error('[bridge] Discord 연결됨');
    pipeServer.sendToUnity({ type: 'bridge_status', status: 'discord_connected' });
  });

  discordClient.on('disconnected', () => {
    console.error('[bridge] Discord 연결 끊김');
    pipeServer.sendToUnity({ type: 'bridge_status', status: 'discord_disconnected' });
  });

  try {
    await discordClient.connect();

  } catch (err) {
    console.error('[bridge] Discord 연결 실패:', err.message);
    pipeServer.sendToUnity({
      type: 'bridge_status',
      status: 'error',
      message: `Discord 연결 실패: ${err.message}`,
    });
    discordClient = null;
    eventRouter = null;
  }
}

// ── 시작 ──
async function main() {
  // MCP 먼저 연결 — Pipe 이벤트가 오기 전에 transport 준비
  const transport = new StdioServerTransport();
  await mcpChannel.server.connect(transport);
  console.error('[bridge] MCP Channel 준비 완료');

  // Named Pipe 서버 시작
  await pipeServer.start();
}

main().catch((err) => {
  console.error('[bridge] 치명적 에러:', err);
  process.exit(1);
});

// ── 종료 처리 ──
process.on('SIGINT', () => {
  console.error('[bridge] 종료 중...');
  if (discordClient) discordClient.disconnect();
  pipeServer.stop();
  process.exit(0);
});

process.on('SIGTERM', () => {
  if (discordClient) discordClient.disconnect();
  pipeServer.stop();
  process.exit(0);
});
