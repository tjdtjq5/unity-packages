import net from 'node:net';
import { EventEmitter } from 'node:events';

/**
 * Named Pipe 서버 — Unity 에디터와의 NDJSON 통신
 * 단일 클라이언트 모드: 한 번에 하나의 Unity 연결만 허용
 */
export class PipeServer extends EventEmitter {
  static MAX_BUFFER_SIZE = 1024 * 1024; // [M3] 1MB 버퍼 제한

  /** @param {string} pipeName - Windows Named Pipe 경로 (\\.\pipe\claude-unity-{hash}) */
  constructor(pipeName) {
    super();
    this.pipeName = pipeName;
    this._server = null;
    this._client = null;
    this._buffer = '';
  }

  /** Named Pipe 서버 시작 */
  async start() {
    return new Promise((resolve, reject) => {
      this._server = net.createServer((socket) => {
        // 단일 클라이언트: 기존 연결 있으면 새 연결 거부
        if (this._client) {
          console.error('[pipe] 기존 연결이 있어 새 연결 거부');
          socket.destroy();
          return;
        }

        this._client = socket;
        this._buffer = '';
        console.error('[pipe] Unity 연결됨');
        this.emit('connect');

        socket.on('data', (data) => {
          this._buffer += data.toString('utf8');
          // [M3] 버퍼 크기 초과 시 클리어 (줄바꿈 없는 잘못된 데이터 방지)
          if (this._buffer.length > PipeServer.MAX_BUFFER_SIZE) {
            console.error('[pipe] 버퍼 크기 초과, 클리어');
            this._buffer = '';
          }
          this._processBuffer();
        });

        socket.on('end', () => {
          console.error('[pipe] Unity 연결 끊김');
          this._client = null;
          this._buffer = '';
          this.emit('disconnect');
        });

        socket.on('error', (err) => {
          console.error('[pipe] 소켓 에러:', err.message);
          // [L3] 에러 시 소켓 정리
          if (this._client && !this._client.destroyed) this._client.destroy();
          this._client = null;
          this._buffer = '';
          this.emit('disconnect');
        });
      });

      this._server.on('error', (err) => {
        console.error('[pipe] 서버 에러:', err.message);
        reject(err);
      });

      this._server.listen(this.pipeName, () => {
        console.error(`[pipe] 리스닝: ${this.pipeName}`);
        resolve();
      });
    });
  }

  /** Named Pipe 서버 종료 */
  stop() {
    if (this._client) {
      this._client.destroy();
      this._client = null;
    }
    if (this._server) {
      this._server.close();
      this._server = null;
    }
  }

  /** Unity에 메시지 전송 (Bridge → Unity) */
  sendToUnity(obj) {
    if (!this._client || this._client.destroyed) return false;
    try {
      this._client.write(JSON.stringify(obj) + '\n');
      return true;
    } catch (err) {
      console.error('[pipe] 전송 실패:', err.message);
      return false;
    }
  }

  /** Unity 연결 여부 */
  get isConnected() {
    return this._client !== null && !this._client.destroyed;
  }

  /** NDJSON 버퍼 처리 — 줄바꿈 기준으로 JSON 파싱 */
  _processBuffer() {
    let newlineIdx;
    while ((newlineIdx = this._buffer.indexOf('\n')) !== -1) {
      const line = this._buffer.slice(0, newlineIdx).trim();
      this._buffer = this._buffer.slice(newlineIdx + 1);

      if (!line) continue;

      try {
        const msg = JSON.parse(line);
        this.emit('message', msg);
      } catch (err) {
        console.error('[pipe] JSON 파싱 실패:', line.slice(0, 100));
      }
    }
  }
}
