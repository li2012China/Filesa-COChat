"""
Filesa COChat 5.1 协议参考实现（仅报文编解码，不含网络与 UI）

来源: FilesaCOChat.exe 内 main.pyc 的字节码常量与反汇编（静态分析）。
性质: 根据 shipped artifact 重建的参考骨架，未经运行时验证；原实现为
      "裸字符串 + recv(1024)" 的无帧协议，本文件同时给出建议的帧格式。

注意: 本模块不会打开 socket、不会执行 shell 命令、不会启动 GUI。
      COMMAND 报文仅作为历史协议事实保留，切勿在真实环境中启用。

配套文档: FilesaCOChat_协议参考.md / FilesaCOChat_逆向分析报告.html
"""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import os
import struct
from typing import Optional

# --- 观测到的端口（main.py: FilesaCOChat.__init__, line 297） ---
PORT_CHAT = 11451          # TCP, 聊天与控制
PORT_DISCOVERY = 11452     # UDP, 广播发现
PORT_FILE_TRANSFER = 11450 # TCP, 文件传输
APP_VERSION = "5.1"
BROADCAST_ADDR = ("255.255.255.255", PORT_DISCOVERY)
GITHUB_RELEASES_API = (
    "https://api.github.com/repos/li2012China/Filesa-COChat/releases/latest"
)

# --- 报文前缀（main.py: receive_messages, line 885） ---
P_DISCOVERY = "COCHAT_DISCOVERY:"
P_RESPONSE = "COCHAT_RESPONSE:"
P_JOIN = "JOIN:"
P_USERLIST = "USERLIST:"
P_USERLIST_REQUEST = "USERLIST_REQUEST"
P_USERNAME_EXISTS = "USERNAME_EXISTS"
P_MIGRATE = "MIGRATE:"
P_PROMOTE = "PROMOTE:"
P_COMMAND = "COMMAND:"

ACCEPT = b"ACCEPT"
REJECT = b"REJECT"
SEND_HASH = b"SEND_HASH"
CHAT_RECV_SIZE = 1024
FILE_CHUNK_SIZE = 4096
COMMAND_TIMEOUT = 5        # subprocess.run(timeout=5) 在原实现中的取值


# --------------------------------------------------------------------------
# 1. 原协议: 报文构造（与原实现中的 f-string 拼接一致）
# --------------------------------------------------------------------------
def discovery_request(port: int = PORT_CHAT) -> bytes:
    return f"{P_DISCOVERY}{port}".encode()


def discovery_response(port: int = PORT_CHAT) -> bytes:
    return f"{P_RESPONSE}{port}".encode()


def is_discovery_request(data: bytes) -> bool:
    return data.decode(errors="replace").startswith(P_DISCOVERY)


def is_expected_response(data: bytes, port: int = PORT_CHAT) -> bool:
    # 原实现使用全等比较，不是 startswith
    return data.decode(errors="replace") == f"{P_RESPONSE}{port}"


def join(username: str) -> bytes:
    return f"{P_JOIN}{username}".encode()


def userlist(users: list[str]) -> bytes:
    return f"{P_USERLIST}{','.join(users)}".encode()


def userlist_request() -> bytes:
    return P_USERLIST_REQUEST.encode()


def chat(username: str, text: str) -> bytes:
    return f"{username}:{text}".encode()


def migrate(new_server_ip: str) -> bytes:
    return f"{P_MIGRATE}{new_server_ip}".encode()


def promote(old_server_ip: str) -> bytes:
    return f"{P_PROMOTE}{old_server_ip}".encode()


def command(target_user: str, shell_command: str) -> bytes:
    """历史协议事实。原接收端会执行 shell=True 的 subprocess.run，存在 RCE 风险。

    这里只构造字符串，绝不执行。新实现应删除此报文类型。
    """
    return f"{P_COMMAND}{target_user}:{shell_command}".encode()


def file_info(filename: str, file_size: int) -> bytes:
    return f"{filename}|{file_size}".encode()


def parse_file_info(data: bytes) -> tuple[str, int]:
    name, size = data.decode(errors="replace").split("|", 1)
    return name, int(size)


def sha256_hex(path: str) -> str:
    """与原实现一致: 分块读取后返回 hexdigest。"""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def safe_receive_path(receive_dir: str, filename: str) -> str:
    """建议替代原实现的 os.path.join(receive_dir, filename)。

    原实现未对对端提供的 filename 做清洗，存在路径穿越面。
    """
    name = os.path.basename(filename.replace("\\", "/"))
    if not name or name in {".", ".."}:
        raise ValueError("invalid filename")
    return os.path.join(receive_dir, name)


# --------------------------------------------------------------------------
# 2. 原协议: 报文分派（按 receive_messages 的分支顺序重建）
# --------------------------------------------------------------------------
@dataclass(frozen=True)
class Parsed:
    kind: str
    payload: object = None


def parse_message(raw: bytes) -> Parsed:
    """把一条 recv 结果归类。注意: 原协议没有帧边界，单次 recv 可能拿到
    半条或多条报文，本函数无法修复这一点，仅用于说明分支优先级。
    """
    msg = raw.decode(errors="replace")

    if msg.startswith(P_JOIN):
        return Parsed("join", msg[len(P_JOIN):])
    if msg == P_USERNAME_EXISTS:
        return Parsed("username_exists")
    if msg == P_USERLIST_REQUEST:
        return Parsed("userlist_request")
    if msg.startswith(P_USERLIST):
        users = msg[len(P_USERLIST):].split(",") if msg[len(P_USERLIST):] else []
        return Parsed("userlist", users)
    if msg.startswith(P_MIGRATE):
        return Parsed("migrate", msg[len(P_MIGRATE):])
    if msg.startswith(P_PROMOTE):
        return Parsed("promote", msg[len(P_PROMOTE):])
    if msg.startswith(P_COMMAND):
        parts = msg.split(":", 2)   # ['COMMAND', target_user, shell_command]
        if len(parts) == 3:
            return Parsed("command", {"target": parts[1], "command": parts[2]})
        return Parsed("malformed_control", msg)

    parts = msg.split(":", 1)       # 普通聊天: username:text
    if len(parts) == 2:
        return Parsed("chat", {"username": parts[0], "text": parts[1]})
    return Parsed("unknown", msg)


# --------------------------------------------------------------------------
# 3. 建议的新帧格式（不是原协议，用于二次开发）
# --------------------------------------------------------------------------
FRAME_MAGIC = b"FCCH"
FRAME_VERSION = 1
FRAME_HEADER = struct.Struct("!4sBHI")  # magic, version, reserved, body length


def encode_frame(kind: str, **fields) -> bytes:
    body = json.dumps({"type": kind, **fields}, ensure_ascii=False).encode("utf-8")
    return FRAME_HEADER.pack(FRAME_MAGIC, FRAME_VERSION, 0, len(body)) + body


def decode_frame(data: bytes) -> Optional[dict]:
    if len(data) < FRAME_HEADER.size:
        return None
    magic, version, _, length = FRAME_HEADER.unpack_from(data)
    if magic != FRAME_MAGIC or version != FRAME_VERSION:
        raise ValueError("bad frame header")
    body = data[FRAME_HEADER.size:FRAME_HEADER.size + length]
    if len(body) != length:
        return None  # 需要继续 recv
    return json.loads(body.decode("utf-8"))


# --------------------------------------------------------------------------
# 4. 静态分析发现的两处高风险行为（此处仅以注释记录，不实现）
# --------------------------------------------------------------------------
# (1) receive_messages 的 COMMAND: 分支调用:
#       subprocess.run(command, shell=True, capture_output=True, text=True, timeout=5)
#     无认证、无白名单、无来源校验 -> 局域网内可触发任意命令执行。
# (2) receive_messages 的普通消息分支只调用 add_message(username, msg)，
#     未见服务器向其他 client_sockets 转发；客户端又只有一条到服务器的连接，
#     推断客户端之间无法互通，需运行时验证并补上服务端广播。
