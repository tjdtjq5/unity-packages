/**
 * 이벤트 라우터 — 소스별/모드별로 이벤트를 적절한 대상으로 라우팅
 *
 * 라우팅 매트릭스:
 *            │ Claude에 전달  │ Discord에 전달
 * ───────────┼────────────────┼─────────────────
 * Unity 이벤트│ 항상           │ mode≥notify && !muted
 * Discord MSG │ interactive만  │ (이미 Discord에 있음)
 * Claude reply│ ─              │ 항상 (mode≥notify)
 * Claude notif│ ─              │ 항상 (mute 무시)
 */
export class EventRouter {
  /**
   * @param {object} opts
   * @param {import('./mcp-channel.js').McpChannel} opts.mcpChannel
   * @param {import('./discord-client.js').DiscordClient} opts.discordClient
   * @param {import('./pipe-server.js').PipeServer} opts.pipeServer
   */
  constructor({ mcpChannel, discordClient, pipeServer }) {
    this._mcpChannel = mcpChannel;
    this._discordClient = discordClient;
    this._pipeServer = pipeServer;
    this._mode = 'off';  // 'off' | 'notify' | 'interactive'
    this._muted = false;
  }

  // ── 모드 관리 ──

  get mode() { return this._mode; }

  setMode(mode) {
    // 'active'를 'interactive'로 정규화
    if (mode === 'active') mode = 'interactive';
    if (!['off', 'notify', 'interactive'].includes(mode)) return;
    this._mode = mode;
    console.error(`[router] 모드 변경: ${mode}`);
  }

  get isMuted() { return this._muted; }

  setMute(muted) {
    this._muted = muted;
    console.error(`[router] 음소거: ${muted ? 'ON' : 'OFF'}`);
  }

  // ── Unity 이벤트 → Claude + Discord ──

  async handleUnityEvent(event) {
    // 항상 Claude에 전달
    await this._mcpChannel.sendToClaudeFromUnity(event);

    // Discord: notify 이상이고 mute가 아니면 전달
    if (this._mode !== 'off' && !this._muted && this._discordClient.isConnected) {
      const text = formatUnityEventForDiscord(event);
      const category = event.severity === 'error' || event.severity === 'exception'
        ? 'error' : 'info';
      await this._discordClient.sendNotification(text, category);
    }
  }

  // ── Discord 메시지 → Claude ──

  async handleDiscordMessage(msg) {
    if (this._mode === 'interactive') {
      await this._mcpChannel.sendToClaudeFromDiscord(msg.user, msg.content);
    } else if (this._mode === 'notify') {
      await this._discordClient.sendMessage(
        '현재 **알림 모드**입니다. `!mode active`로 전환하세요.'
      );
    }
    // mode === 'off': 무시
  }

  // ── Discord 명령어 ──

  async handleDiscordCommand(cmd) {
    switch (cmd.name) {
      case 'mode': {
        const newMode = cmd.args[0];
        if (!newMode) {
          await this._discordClient.sendMessage(`현재 모드: **${this._mode}**`);
          break;
        }
        this.setMode(newMode);
        await this._discordClient.sendMessage(`모드 변경: **${this._mode}**`);
        // Unity에도 모드 변경 전달
        this._pipeServer.sendToUnity({ type: 'mode_changed', mode: this._mode });
        break;
      }

      case 'mute':
        this.setMute(true);
        await this._discordClient.sendMessage('🔇 음소거 활성화');
        break;

      case 'unmute':
        this.setMute(false);
        await this._discordClient.sendMessage('🔊 음소거 해제');
        break;

      case 'status':
        await this._discordClient.sendMessage(
          `📊 **상태**\n` +
          `• 모드: ${this._mode}\n` +
          `• 음소거: ${this._muted ? 'ON' : 'OFF'}\n` +
          `• Unity: ${this._pipeServer.isConnected ? '연결됨' : '미연결'}`
        );
        break;

      default:
        await this._discordClient.sendMessage(
          `알 수 없는 명령어: \`!${cmd.name}\`\n` +
          '사용 가능: `!mode`, `!mute`, `!unmute`, `!status`'
        );
    }
  }

  // ── Claude → Discord ──

  async handleClaudeReply(text) {
    if (this._mode !== 'off' && this._discordClient.isConnected) {
      await this._discordClient.sendMessage(text);
    }
  }

  async handleClaudeNotification(text, category) {
    // 알림은 mute 상태와 무관하게 전송 (단, mode가 off면 미전송)
    if (this._mode !== 'off' && this._discordClient.isConnected) {
      await this._discordClient.sendNotification(text, category);
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
