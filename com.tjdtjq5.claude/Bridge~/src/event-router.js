/**
 * 이벤트 라우터 — 소스별로 이벤트를 적절한 대상으로 라우팅
 * 항상 양방향 (interactive) 모드로 동작.
 */
export class EventRouter {
  constructor({ mcpChannel, discordClient, pipeServer }) {
    this._mcpChannel = mcpChannel;
    this._discordClient = discordClient;
    this._pipeServer = pipeServer;
  }

  // ── Unity 이벤트 → Claude ──

  async handleUnityEvent(event) {
    await this._mcpChannel.sendToClaudeFromUnity(event);
  }

  // ── Discord 메시지 → Claude ──

  async handleDiscordMessage(msg) {
    await this._mcpChannel.sendToClaudeFromDiscord(msg.user, msg.content);
  }

  // ── Discord 명령어 ──

  async handleDiscordCommand(cmd) {
    switch (cmd.name) {
      case 'status':
        await this._discordClient.sendMessage(
          `📊 **상태**\n` +
          `• Unity: ${this._pipeServer.isConnected ? '연결됨' : '미연결'}`
        );
        break;

      default:
        await this._discordClient.sendMessage(
          `알 수 없는 명령어: \`!${cmd.name}\`\n` +
          '사용 가능: `!status`'
        );
    }
  }

  // ── Claude → Discord ──

  async handleClaudeReply(text) {
    if (this._discordClient.isConnected) {
      await this._discordClient.sendMessage(text);
    }
  }
}

/** Unity 이벤트를 Discord 표시용 텍스트로 포맷 */
function formatUnityEventForDiscord(event) {
  const parts = [];

  if (event.category === 'compile') {
    parts.push(`**컴파일 에러**`);
  }

  parts.push(event.message);

  if (event.sourceFile) {
    parts.push(`\`${event.sourceFile}:${event.sourceLine || ''}\``);
  }

  if (event.repeatCount > 1) {
    parts.push(`(${event.repeatCount}회 반복)`);
  }

  return parts.join('\n');
}
