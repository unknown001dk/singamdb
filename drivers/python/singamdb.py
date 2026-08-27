# singamdb - Official Python Native Driver for SingamDB Wire Protocol
import socket
import struct
import json
import zlib

MAGIC = 0x534E474D  # 'SNGM'

class MessageType:
    PING = 1
    PONG = 2
    HANDSHAKE = 3
    INSERT = 10
    FIND = 11
    GET_BY_ID = 12
    UPDATE = 13
    DELETE = 14
    CREATE_INDEX = 20
    AGGREGATE = 30
    EXPLAIN = 40
    RESPONSE_OK = 100
    RESPONSE_ERROR = 101

class SingamClient:
    def __init__(self, host="localhost", port=7778):
        if "://" in str(host):
            clean = host.replace("singam://", "")
            parts = clean.split(":")
            self.host = parts[0]
            self.port = int(parts[1]) if len(parts) > 1 else 7778
        else:
            self.host = host
            self.port = port
        self.sock = None
        self.req_id_counter = 1

    def connect(self):
        if self.sock is None:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            self.sock.connect((self.host, self.port))
        return self

    def _send(self, msg_type, payload_dict):
        self.connect()
        req_id = self.req_id_counter
        self.req_id_counter += 1

        payload_bytes = json.dumps(payload_dict).encode("utf-8")
        payload_len = len(payload_bytes)

        # Header: Magic(4), MsgType(4), ReqId(4), PayloadLen(4)
        header = struct.pack(">IIII", MAGIC, msg_type, req_id, payload_len)
        frame_without_crc = header + payload_bytes
        crc = zlib.crc32(frame_without_crc) & 0xFFFFFFFF
        full_frame = frame_without_crc + struct.pack(">I", crc)

        self.sock.sendall(full_frame)

        # Read Response Header
        resp_header = self._recv_exact(16)
        r_magic, r_type, r_req, r_len = struct.unpack(">IIII", resp_header)
        if r_magic != MAGIC:
            raise ValueError("Invalid SingamDB wire frame header magic.")

        resp_payload_and_crc = self._recv_exact(r_len + 4)
        resp_payload = resp_payload_and_crc[:r_len]
        received_crc = struct.unpack(">I", resp_payload_and_crc[r_len:])[0]

        # Verify CRC
        computed_crc = zlib.crc32(resp_header + resp_payload) & 0xFFFFFFFF
        if computed_crc != received_crc:
            raise ValueError("CRC32 checksum mismatch in server response.")

        resp_str = resp_payload.decode("utf-8")
        try:
            parsed = json.loads(resp_str)
            if r_type == MessageType.RESPONSE_ERROR:
                raise RuntimeError(parsed.get("error", "Database server error"))
            return parsed
        except json.JSONDecodeError:
            return resp_str

    def _recv_exact(self, length):
        data = bytearray()
        while len(data) < length:
            chunk = self.sock.recv(length - len(data))
            if not chunk:
                raise ConnectionError("Server disconnected unexpectedly.")
            data.extend(chunk)
        return bytes(data)

    def database(self, db_name):
        return SingamDatabase(self, db_name)

    def __getitem__(self, db_name):
        return self.database(db_name)

    def close(self):
        if self.sock:
            self.sock.close()
            self.sock = None

class SingamDatabase:
    def __init__(self, client, name):
        self.client = client
        self.name = name

    def collection(self, coll_name):
        return SingamCollection(self.client, self.name, coll_name)

    def __getitem__(self, coll_name):
        return self.collection(coll_name)

class SingamCollection:
    def __init__(self, client, db_name, coll_name):
        self.client = client
        self.db_name = db_name
        self.coll_name = coll_name

    def insert_one(self, document, custom_id=None):
        return self.client._send(MessageType.INSERT, {
            "Database": self.db_name,
            "Collection": self.coll_name,
            "Document": document,
            "CustomId": custom_id
        })

    def find(self, filter_dict=None):
        return SingamCursor(self.client, self.db_name, self.coll_name, filter_dict or {})

    def find_one(self, filter_dict=None):
        res = self.find(filter_dict).limit(1).to_list()
        return res[0] if res else None

    def get_by_id(self, doc_id):
        return self.client._send(MessageType.GET_BY_ID, {
            "Database": self.db_name,
            "Collection": self.coll_name,
            "DocId": doc_id
        })

    def update_one(self, doc_id, update_dict, merge=True):
        return self.client._send(MessageType.UPDATE, {
            "Database": self.db_name,
            "Collection": self.coll_name,
            "DocId": doc_id,
            "UpdateData": update_dict,
            "Merge": merge
        })

    def delete_one(self, doc_id):
        return self.client._send(MessageType.DELETE, {
            "Database": self.db_name,
            "Collection": self.coll_name,
            "DocId": doc_id
        })

    def create_index(self, field, is_btree=False):
        is_composite = isinstance(field, list)
        return self.client._send(MessageType.CREATE_INDEX, {
            "Database": self.db_name,
            "Collection": self.coll_name,
            "Field": field if not is_composite else None,
            "IsBTree": is_btree,
            "IsComposite": is_composite,
            "Fields": field if is_composite else None
        })

    def aggregate(self, pipeline_dict, filter_dict=None):
        return self.client._send(MessageType.AGGREGATE, {
            "Database": self.db_name,
            "Collection": self.coll_name,
            "Request": pipeline_dict,
            "Filter": filter_dict
        })

class SingamCursor:
    def __init__(self, client, db_name, coll_name, filter_dict):
        self.client = client
        self.db_name = db_name
        self.coll_name = coll_name
        self.filter = filter_dict
        self._sort_field = None
        self._ascending = True
        self._project_fields = None
        self._limit = 100
        self._skip = 0

    def sort(self, field, order=1):
        self._sort_field = field
        self._ascending = order >= 0
        return self

    def project(self, *fields):
        self._project_fields = list(fields)
        return self

    def limit(self, count):
        self._limit = count
        return self

    def skip(self, count):
        self._skip = count
        return self

    def to_list(self):
        return self.client._send(MessageType.FIND, {
            "Database": self.db_name,
            "Collection": self.coll_name,
            "Filter": self.filter,
            "SortField": self._sort_field,
            "Ascending": self._ascending,
            "ProjectFields": self._project_fields,
            "Limit": self._limit,
            "Skip": self._skip
        })

    def __iter__(self):
        return iter(self.to_list())
