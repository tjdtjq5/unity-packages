import { Client, GatewayIntentBits } from 'discord.js';
import { EventEmitter } from 'node:events';

const CATEGORY_PREFIX = {
  error:  '❌',
  build:  '📦',
  commit: '✅',
  info:   'ℹ️',
};

const MAX_MESSAGE_LENGTH = 2000;

/**
 * Discord 봇 클라이언트 — 특정 채널에서 허용된 사용자의 메시지를 수신/발신
 */
export class DiscordClient extends EventEmitter {
  /**
   * @param {object} opts
   * @param {string} opts.token - Discord Bot Token
   * @param {string} opts.channelId - 리스닝 채널 ID
   * @param {string[]} opts.allowedUsers - 허용 사용자 ID 목록
   */
  constructor({ token, channelId, allowedUsers }) {
    super();
    this._token = token;
    this._channelId = channelId;
    this._allowedUsers = new Set(allowedUsers || []);
    this._client = null;
    this._channel = null;
  }

  /** Discord 봇 로그인 + 채널 리스닝. ready 이벤트까지 대기. */
  async connect() {
    this._client = new Client({
      intents: [
        GatewayIntentBits.Guilds,
        GatewayIntentBits.GuildMessages,
        GatewayIntentBits.MessageContent,
      ],
    });

    // [P2-3] login + ready를 하나의 Promise로 묶어서 channel 준비 보장
    await new Promise((resolve, reject) => {
      this._client.once('ready', async () => {
        console.error(`[discord] 로그인: ${this._client.user.tag}`);
        this._channel = this._client.channels.cache.get(this._channelId);
        if (!this._channel) {
          try {
            this._channel = await this._client.channels.fetch(this._channelId);
          } catch (err) {
            console.error('[discord] 채널 찾기 실패:', err.message);
          }
        }
        this.emit('connected');
        resolve();
      });

      this._client.login(this._token).catch(reject);
    });

    this._client.on('messageCreate', (msg) => {
      // 봇 자신 무시
      if (msg.author.bot) return;

      // 지정 채널만
      if (msg.channelId !== this._channelId) return;

      // 허용된 사용자만 (목록이 비어있으면 전체 허용)
      if (this._allowedUsers.size > 0 && !this._allowedUsers.has(msg.author.id)) return;

      // 명령어 처리
      if (msg.content.startsWith('!')) {
        const cmd = parseCommand(msg.content);
        this.emit('command', cmd);
        return;
      }

      // 일반 메시지
      this.emit('message', {
        user: msg.author.username,
        userId: msg.author.id,
        content: msg.content,
      });
    });

    this._client.on('error', (err) => {
      console.error('[discord] 에러:', err.message);
    });

    // [P2-1] discord.js v14: shardDisconnect / shardError 사용
    this._client.on('shardDisconnect', (event, shardId) => {
      console.error(`[discord] 샤드 ${shardId} 연결 끊김 (code: ${event.code})`);
      this.emit('disconnected');
    });

    this._client.on('shardError', (err, shardId) => {
      console.error(`[discord] 샤드 ${shardId} 에러:`, err.message);
    });

    this._client.on('shardReconnecting', (shardId) => {
      console.error(`[discord] 샤드 ${shardId} 재연결 중...`);
    });
  }

  /** 연결 해제 */
  async disconnect() {
    if (this._client) {
      this._client.destroy();
      this._client = null;
      this._channel = null;
    }
  }

  /** 일반 메시지 전송 (2000자 자동 분할) */
  async sendMessage(text) {
    if (!this._channel) return;

    const chunks = splitMessage(text);
    for (const chunk of chunks) {
      try {
        await this._channel.send(chunk);
      } catch (err) {
        console.error('[discord] 전송 실패:', err.message);
      }
    }
  }

  /** 카테고리별 이모지 prefix 포함 알림 */
  async sendNotification(text, category) {
    const prefix = CATEGORY_PREFIX[category] || CATEGORY_PREFIX.info;
    await this.sendMessage(`${prefix} ${text}`);
  }

  /** 연결 상태 */
  get isConnected() {
    return this._client !== null && this._client.isReady();
  }
}

/** 명령어 파서: "!mode active" → { name: 'mode', args: ['active'] } */
function parseCommand(content) {
  const parts = content.slice(1).trim().split(/\s+/);
  return { name: parts[0].toLowerCase(), args: parts.slice(1) };
}

/** 2000자 기준 메시지 분할 (코드블록 경계 유지) */
function splitMessage(text) {
  if (text.length <= MAX_MESSAGE_LENGTH) return [text];

  const chunks = [];
  let remaining = text;

  while (remaining.length > 0) {
    if (remaining.length <= MAX_MESSAGE_LENGTH) {
      chunks.push(remaining);
      break;
    }

    // 줄바꿈 기준으로 자르기
    let cutAt = remaining.lastIndexOf('\n', MAX_MESSAGE_LENGTH);
    if (cutAt < MAX_MESSAGE_LENGTH * 0.5) cutAt = MAX_MESSAGE_LENGTH; // 너무 짧으면 강제 컷

    chunks.push(remaining.slice(0, cutAt));
    remaining = remaining.slice(cutAt);
  }

  return chunks;
}
