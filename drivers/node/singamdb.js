// singamdb - Official Node.js Native Driver for SingamDB Wire Protocol
const net = require('net');

const MAGIC = 0x534e474d; // 'SNGM'

const MessageType = {
  Ping: 1,
  Pong: 2,
  Handshake: 3,
  Insert: 10,
  Find: 11,
  GetById: 12,
  Update: 13,
  Delete: 14,
  CreateIndex: 20,
  Aggregate: 30,
  Explain: 40,
  ResponseOk: 100,
  ResponseError: 101
};

// Fast CRC32 table
const crcTable = new Uint32Array(256);
for (let i = 0; i < 256; i++) {
  let c = i;
  for (let k = 0; k < 8; k++) {
    c = ((c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1));
  }
  crcTable[i] = c;
}

function crc32(buf) {
  let crc = 0 ^ (-1);
  for (let i = 0; i < buf.length; i++) {
    crc = (crc >>> 8) ^ crcTable[(crc ^ buf[i]) & 0xFF];
  }
  return (crc ^ (-1)) >>> 0;
}

class SingamClient {
  constructor(url = 'singam://localhost:7778') {
    const parsed = new URL(url.replace('singam://', 'http://'));
    this.host = parsed.hostname || 'localhost';
    this.port = parseInt(parsed.port || '7778', 10);
    this.socket = null;
    this.reqIdCounter = 1;
    this.pendingRequests = new Map();
    this.buffer = Buffer.alloc(0);
    this.connected = false;
  }

  async connect() {
    return new Promise((resolve, reject) => {
      this.socket = net.createConnection({ host: this.host, port: this.port }, () => {
        this.connected = true;
        this.socket.setNoDelay(true);
        resolve(this);
      });

      this.socket.on('data', (chunk) => this._onData(chunk));
      this.socket.on('error', (err) => reject(err));
      this.socket.on('close', () => { this.connected = false; });
    });
  }

  _onData(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);

    while (this.buffer.length >= 16) {
      const magic = this.buffer.readUInt32BE(0);
      if (magic !== MAGIC) {
        throw new Error('Invalid SingamDB wire frame header magic.');
      }

      const msgType = this.buffer.readInt32BE(4);
      const reqId = this.buffer.readUInt32BE(8);
      const payloadLen = this.buffer.readInt32BE(12);

      const totalFrameLen = 16 + payloadLen + 4;
      if (this.buffer.length < totalFrameLen) {
        break; // Wait for full frame
      }

      const payload = this.buffer.slice(16, 16 + payloadLen);
      const receivedCrc = this.buffer.readUInt32BE(16 + payloadLen);

      // Verify CRC32
      const frameWithoutCrc = this.buffer.slice(0, 16 + payloadLen);
      const computedCrc = crc32(frameWithoutCrc);

      this.buffer = this.buffer.slice(totalFrameLen);

      const handler = this.pendingRequests.get(reqId);
      if (handler) {
        this.pendingRequests.delete(reqId);
        if (computedCrc !== receivedCrc) {
          handler.reject(new Error('CRC32 checksum mismatch in server response.'));
          continue;
        }

        const jsonStr = payload.toString('utf8');
        try {
          const parsed = JSON.parse(jsonStr);
          if (msgType === MessageType.ResponseError) {
            handler.reject(new Error(parsed.error || 'Server error'));
          } else {
            handler.resolve(parsed);
          }
        } catch (e) {
          handler.resolve(jsonStr);
        }
      }
    }
  }

  async _send(msgType, payloadObj) {
    if (!this.connected) await this.connect();

    const reqId = this.reqIdCounter++;
    const payloadStr = JSON.stringify(payloadObj);
    const payloadBytes = Buffer.from(payloadStr, 'utf8');

    const totalLen = 16 + payloadBytes.length + 4;
    const frame = Buffer.alloc(totalLen);

    frame.writeUInt32BE(MAGIC, 0);
    frame.writeInt32BE(msgType, 4);
    frame.writeUInt32BE(reqId, 8);
    frame.writeInt32BE(payloadBytes.length, 12);
    payloadBytes.copy(frame, 16);

    const crc = crc32(frame.slice(0, totalLen - 4));
    frame.writeUInt32BE(crc, totalLen - 4);

    return new Promise((resolve, reject) => {
      this.pendingRequests.set(reqId, { resolve, reject });
      this.socket.write(frame);
    });
  }

  database(dbName) {
    return new SingamDatabase(this, dbName);
  }

  close() {
    if (this.socket) {
      this.socket.end();
      this.connected = false;
    }
  }
}

class SingamDatabase {
  constructor(client, name) {
    this.client = client;
    this.name = name;
  }

  collection(collName) {
    return new SingamCollection(this.client, this.name, collName);
  }
}

class SingamCollection {
  constructor(client, dbName, collName) {
    this.client = client;
    this.dbName = dbName;
    this.collName = collName;
  }

  async insertOne(document, customId = null) {
    return this.client._send(MessageType.Insert, {
      Database: this.dbName,
      Collection: this.collName,
      Document: document,
      CustomId: customId
    });
  }

  async insertMany(documents) {
    const results = [];
    for (const doc of documents) {
      results.push(await this.insertOne(doc));
    }
    return results;
  }

  find(filter = {}) {
    return new SingamCursor(this.client, this.dbName, this.collName, filter);
  }

  async findOne(filter = {}) {
    const results = await this.find(filter).limit(1).toArray();
    return results.length > 0 ? results[0] : null;
  }

  async getById(docId) {
    return this.client._send(MessageType.GetById, {
      Database: this.dbName,
      Collection: this.collName,
      DocId: docId
    });
  }

  async updateOne(docId, updateData, merge = true) {
    return this.client._send(MessageType.Update, {
      Database: this.dbName,
      Collection: this.collName,
      DocId: docId,
      UpdateData: updateData,
      Merge: merge
    });
  }

  async deleteOne(docId) {
    return this.client._send(MessageType.Delete, {
      Database: this.dbName,
      Collection: this.collName,
      DocId: docId
    });
  }

  async createIndex(field, options = {}) {
    return this.client._send(MessageType.CreateIndex, {
      Database: this.dbName,
      Collection: this.collName,
      Field: Array.isArray(field) ? null : field,
      IsBTree: !!options.isBTree,
      IsUnique: !!options.unique,
      IsComposite: Array.isArray(field),
      Fields: Array.isArray(field) ? field : null
    });
  }

  async aggregate(pipelineRequest, filter = null) {
    return this.client._send(MessageType.Aggregate, {
      Database: this.dbName,
      Collection: this.collName,
      Request: pipelineRequest,
      Filter: filter
    });
  }

  async explain(filter = {}) {
    return this.client._send(MessageType.Explain, {
      Database: this.dbName,
      Collection: this.collName,
      Filter: filter
    });
  }
}

class SingamCursor {
  constructor(client, dbName, collName, filter) {
    this.client = client;
    this.dbName = dbName;
    this.collName = collName;
    this.filter = filter;
    this._sortField = null;
    this._ascending = true;
    this._projectFields = null;
    this._limit = 100;
    this._skip = 0;
  }

  sort(fieldOrObj, order = 1) {
    if (typeof fieldOrObj === 'object') {
      const keys = Object.keys(fieldOrObj);
      if (keys.length > 0) {
        this._sortField = keys[0];
        this._ascending = fieldOrObj[keys[0]] >= 0;
      }
    } else {
      this._sortField = fieldOrObj;
      this._ascending = order >= 0;
    }
    return this;
  }

  project(...fields) {
    this._projectFields = fields.flat();
    return this;
  }

  limit(num) {
    this._limit = num;
    return this;
  }

  skip(num) {
    this._skip = num;
    return this;
  }

  async toArray() {
    return this.client._send(MessageType.Find, {
      Database: this.dbName,
      Collection: this.collName,
      Filter: this.filter,
      SortField: this._sortField,
      Ascending: this._ascending,
      ProjectFields: this._projectFields,
      Limit: this._limit,
      Skip: this._skip
    });
  }
}

module.exports = {
  SingamClient,
  SingamDatabase,
  SingamCollection,
  MessageType
};
