import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import {
  ListToolsRequestSchema,
  CallToolRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';

/**
 * MCP Channel — Claude Code와 stdio로 통신하는 Channel 서버.
 *
 * - Unity 이벤트 / Discord 메시지 → Claude에 notification push
 * - Claude의 reply / set_cooldown 도구 호출 처리
 */
export class McpChannel {
  /**
   * @param {object} opts
   * @param {(name: string, args: Record<string, unknown>) => Promise<string>} opts.onToolCall
   *   Claude가 도구를 호출했을 때 실행될 콜백
   */
  constructor({ onToolCall }) {
    this._onToolCall = onToolCall;

    this.server = new Server(
      { name: 'claude-unity-bridge', version: '1.0.0' },
      {
        capabilities: {
          experimental: { 'claude/channel': {} },
          tools: {},
        },
        instructions: [
          'Unity 에디터와 연결된 채널입니다.',
          '',
          '## Discord 대화',
          'Discord에서 사용자의 작업 지시가 전달됩니다.',
          '터미널에서 직접 대화하는 것과 동일하게 응답해주세요.',
          '작업 결과는 reply 도구로 Discord에 보내주세요.',
          '',
          '## Unity 이벤트 (향후)',
          'Unity에서 발생하는 에러, 컴파일 결과를 수신할 수 있습니다.',
          '에러를 수신하면 해당 소스 파일을 분석하고 수정해주세요.',
          '수정 후 set_cooldown 도구로 해당 소스 파일의 쿨다운을 설정해주세요.',
        ].join('\n'),
      },
    );

    this._registerTools();
  }

  // ── Unity 이벤트 → Claude ──

  async sendToClaudeFromUnity(event) {
    const lines = [];

    if (event.severity) lines.push(`[${event.severity.toUpperCase()}] ${event.message}`);
    else lines.push(event.message);

    if (event.stackTrace) lines.push(event.stackTrace);
    if (event.sourceFile) lines.push(`\n소스 파일: ${event.sourceFile}:${event.sourceLine || ''}`);
    if (event.repeatCount > 1) lines.push(`(${event.repeatCount}회 반복)`);

    // [M4] 카테고리/심각도별 지시문 분기
    const category = event.category || 'console';
    if (category === 'compile') {
      lines.push('\n컴파일 에러입니다. 해당 파일을 수정해주세요.');
    } else if (event.severity === 'warning') {
      lines.push('\n경고입니다. 반복되는 패턴이면 근본 원인을 분석해주세요.');
    } else if (event.severity === 'info') {
      // 반복 요약 등 — 별도 지시 불필요
    } else if (event.severity === 'log') {
      // 일반 로그 — 참고용, 수정 지시 불필요
    } else {
      lines.push('\n이 에러를 분석하고 수정해주세요.');
    }

    const meta = { category };
    if (event.sourceFile) meta.source_file = event.sourceFile;

    await this.server.notification({
      method: 'notifications/claude/channel',
      params: {
        content: lines.join('\n'),
        meta,
      },
    });

    console.error(`[mcp] Unity 이벤트 → Claude: ${event.category}/${event.severity}`);
  }

  // ── Discord 메시지 → Claude ──

  async sendToClaudeFromDiscord(user, message) {
    await this.server.notification({
      method: 'notifications/claude/channel',
      params: {
        content: message,
        meta: {
          category: 'discord',
          user,
        },
      },
    });

    console.error(`[mcp] Discord → Claude: ${user}: ${message.slice(0, 50)}`);
  }

  // ── 도구 등록 ──

  _registerTools() {
    this.server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: [
        {
          name: 'reply',
          description: 'Discord 채널에 메시지를 보냅니다. Discord에서 온 메시지에 응답할 때 사용하세요.',
          inputSchema: {
            type: 'object',
            properties: {
              text: { type: 'string', description: '보낼 메시지 내용' },
            },
            required: ['text'],
          },
        },
        {
          name: 'set_cooldown',
          description: '특정 소스 파일의 에러 전달 쿨다운을 설정합니다. 수정한 파일에 대해 호출하면 수정 중 같은 에러가 반복 전달되는 것을 방지합니다.',
          inputSchema: {
            type: 'object',
            properties: {
              sourceFile: { type: 'string', description: '쿨다운을 설정할 소스 파일 경로' },
              seconds: { type: 'number', description: '쿨다운 시간(초). 기본 30초' },
            },
            required: ['sourceFile'],
          },
        },
      ],
    }));

    this.server.setRequestHandler(CallToolRequestSchema, async (req) => {
      const { name, arguments: args } = req.params;

      try {
        const result = await this._onToolCall(name, args || {});
        return { content: [{ type: 'text', text: result || 'ok' }] };
      } catch (err) {
        return {
          content: [{ type: 'text', text: `에러: ${err.message}` }],
          isError: true,
        };
      }
    });
  }
}
