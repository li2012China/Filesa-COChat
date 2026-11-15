# Filesa COChat 5.1 协议参考（静态逆向还原）

> 来源：`FilesaCOChat.exe`（SHA-256 `60bd7e21a226f8a67324d1aacfa8aaef49c842c63b8d5b9c1dda6b31c07a80d0`）内 `main.pyc` 的字节码常量与反汇编。
> 性质：**静态推断**，未运行程序、未抓包验证。报文格式为观测到的字符串拼接；时序与语义为推断。
> 上游：https://github.com/li2012China/Filesa-COChat （MIT，main 分支不含 .py 源码）

## 端口

| 端口 | 协议 | 用途 |
| --- | --- | --- |
| 11451 | TCP | 聊天与控制报文，服务器 `bind(('0.0.0.0', 11451))`，`listen(5)` |
| 11452 | UDP | 局域网广播发现，服务器 `bind(('0.0.0.0', 11452))`，客户端 `sendto` 到 `255.255.255.255:11452` |
| 11450 | TCP | 文件传输，每台客户端都监听 `0.0.0.0:11450`，`listen(5)` |
| 443 | HTTPS | 仅出网检查更新：`api.github.com/repos/li2012China/Filesa-COChat/releases/latest` |

## 通用约定

- 载荷为 UTF-8 编码的裸字符串（`str.encode()` / `bytes.decode()` 默认参数）。
- **无魔数、无长度字段、无转义、无校验和、无认证、无加密**。
- 聊天/控制报文由接收方单次 `recv(1024)` 读取，**没有消息边界**；长报文会截断，连续写入会粘连。
- 控制指令以固定前缀 + `:` 分隔识别，聊天报文用 `split(':', 1)` 拆成 `(username, msg)`。用户名或消息内容若以控制前缀开头，会被误解析为控制指令。

## 一、发现（UDP 11452）

```
C -> 255.255.255.255:11452   "COCHAT_DISCOVERY:11451"
S -> <源地址>               "COCHAT_RESPONSE:11451"
```

- 客户端：`SO_BROADCAST=1`，`settimeout(2)`，`time.time()-start < 3` 循环 `recvfrom(1024)`。
- 服务器：仅在 `running and is_server` 时循环；`data.decode().startswith('COCHAT_DISCOVERY:')` 时把响应发回 `addr`。
- 客户端以 `data.decode() == f'COCHAT_RESPONSE:{self.port}'` 全等比较，命中后返回 `addr[0]` 作为服务器 IP。
- 兜底：广播无结果时扫描本机 `/24` 网段（`ipaddress`，50 线程，`socket.connect_ex((ip, 11451))`，超时 0.1s），找到即停（`scan_complete` 事件 + Lock）。

## 二、建立会话（TCP 11451）

```
C -> S   "JOIN:<username>"
S -> C   "USERNAME_EXISTS"                    # 用户名已在 user_list 中（仅服务器判定）
S -> C   "USERLIST:<a>,<b>,<c>"               # 逗号分隔，JOIN 后广播
C -> S   "USERLIST_REQUEST"                   # 加入后主动请求一次
S -> C   "USERLIST:<a>,<b>,<c>"               # send_user_list 单发
```

服务器在 JOIN 成功时：`users[socket] = username`、`user_ips[username] = getpeername()[0]`、`user_list.append(username)`、`update_user_list()`、`broadcast_user_list()`，并写入系统消息「`<user> 加入了聊天室`」。客户端收到 `USERLIST:` 后（仅非服务器）按 `,` 拆分覆盖本地 `user_list` 并刷新 UI。

## 三、聊天报文（TCP 11451）

```
任意方向   "<username>:<text>"
```

- 发送方 `send_message()`：本地 `add_message(self.username, text)`，然后遍历 `self.client_sockets` 逐个 `send(f'{username}:{text}'.encode())`。
- 接收方 `receive_messages()` 默认分支：`parts = message.split(':', 1)`，`len(parts)==2` 时 `add_message(*parts)`。
- ⚠️ **静态分析未发现服务器转发普通消息的分支**：`receive_messages` 全函数只有两处 `send`（`USERNAME_EXISTS` 回复、`send_user_list`），默认分支只本地显示。客户端又只有一条到服务器的连接（`join_chatroom`）。据此推断：服务器发的消息能到所有客户端，客户端发的消息只到服务器，客户端之间互不可见。**建议用两个客户端实测确认**。

## 四、控制报文（TCP 11451）

| 报文 | 触发方 | 接收方行为 |
| --- | --- | --- |
| `USERNAME_EXISTS` | S | 弹本地系统消息「用户名已存在，请重新启动并选择其他用户名」，记录错误日志 |
| `USERLIST_REQUEST` | C | 服务器调用 `send_user_list(client_socket)` |
| `USERLIST:<csv>` | S | 非服务器客户端覆盖本地用户列表并刷新 UI |
| `MIGRATE:<new_server_ip>` | S（退出时） | `handle_migration`：`migrating=True` → `disconnect_all()` → `join_chatroom(new_ip)`；失败则 5s 后 `reconnect_to_server()` |
| `PROMOTE:<old_server_ip>` | S（退出时） | `handle_promotion`：`disconnect_all()` → `create_chatroom()` 成为新服务器 |
| `COMMAND:<target_user>:<shell command>` | 任意客户端 | 若 `target_user == self.username`，执行 `subprocess.run(command, shell=True, capture_output=True, text=True, timeout=5)`，把 stdout/stderr 以「[命令结果]」显示在**本机**聊天区 |

> **安全提示**：`COMMAND:` 无任何认证、无来源校验、无命令白名单。任何能连上 11451 的局域网主机都可以伪造 `target_user` 让某个客户端执行 shell 命令。这是本次分析发现的最高风险项。

## 五、服务器迁移（S 退出时）

```
initiate_migration()
  new_server_username = select_new_server()   # 从 user_list 随机挑一个 != 自己的用户
  new_server_socket   = 从 self.users 中反查该用户的 socket
  new_server_ip       = new_server_socket.getpeername()[0]
  向 new_server_socket 单发  "PROMOTE:<self.host>"
  向其余所有连接广播       "MIGRATE:<new_server_ip>"
  time.sleep(1); 清理断线 socket
```

无任期号、无确认应答、无回滚；固定 1 秒延迟是唯一时序保护。

## 六、文件传输（TCP 11450）

```
发送端 connect((target_ip, 11450))，settimeout(10)
发送端 -> 接收端   "<filename>|<file_size>"                # 单次 send，'|' 分隔
接收端弹出 askyesno 确认框（KB/MB 自适应显示）
接收端 -> 发送端   b'REJECT'  或  b'ACCEPT'                # recv(1024) 比较
ACCEPT 后：
  发送端 -> 接收端   4096 字节分块，直到累计 >= file_size
  接收端 -> 发送端   b'SEND_HASH'
  发送端 -> 接收端   "<sha256 hexdigest>"                  # calculate_file_hash(filepath)
  接收端本地计算 sha256 并比较，显示 ✓ / ✗ 提示
```

- 接收目录 `files/`（相对 `base_dir`）；文件重名时按 `name_1.ext`、`name_2.ext` 递增。
- 发送端用 `os.path.basename(filepath)` 取文件名；**接收端未见对端 filename 的清洗**，`os.path.join(file_receive_dir, filename)` 存在路径穿越面。
- 无大小上限、无分块序号、无断点续传；校验失败只提示，不重传。
- 目标 IP 来自服务器端维护的 `user_ips[username]`；`send_file_to_user` 取不到时回退本机 IP。

## 七、更新检查（HTTPS）

- `requests.get('https://api.github.com/repos/li2012China/Filesa-COChat/releases/latest', timeout=5)`，取 `tag_name`，去掉前缀 `v` 后与 `self.version`（`5.1`）比较。
- 启动时 `root.after(5000, auto_check_update)`；有更新且 `notifications_enabled` 时用 `PopupNotification` 弹窗「检测到更新，请在设置中更新。」
- `download_update` / `open_github` 只是打开网页（`webbrowser`），不下载、不校验、不安装。

## 八、给二次开发者的重构建议

1. 帧格式：`4B magic + 2B version + 4B length + JSON body`，按长度循环 `recv`，彻底解决粘包与协议注入。
2. 控制面与数据面分离：控制报文用独立 `type` 字段，禁止聊天文本触发控制逻辑。
3. 认证：预共享密钥 + HMAC，或 TLS；服务端分配会话 ID，不再相信客户端自报用户名。
4. 消息路由：服务器显式广播普通消息（排除来源 socket），补多客户端集成测试。
5. 删除 `COMMAND:` 特性；如保留，改为本地显式开关 + 命令白名单 + 交互式确认，且不使用 `shell=True`。
6. 文件传输：强制 `basename` + 字符白名单，限制大小，分块带序号与校验，失败可重传。
7. 迁移：引入任期号、确认应答与超时重选，替代固定 `sleep(1)`。
8. 依赖瘦身：业务代码未使用 `cryptography` / `bcrypt`，Pillow 的 AVIF 插件约 7.8 MB 也可按需裁剪。

## 附：可复核的原始材料

| 文件 | 内容 |
| --- | --- |
| `.workbuddy/analysis/extracted/main.pyc` | 还原出的入口脚本字节码 |
| `.workbuddy/analysis/main_dis.txt` | 全部 81 个代码对象的反汇编 |
| `.workbuddy/analysis/dis/*.txt` | 34 个协议相关函数的单独反汇编 |
| `.workbuddy/analysis/main_strings_by_func.txt` | 按函数分组的常量、co_names、co_varnames |
| `.workbuddy/analysis/main_structure.json` | 代码对象清单（名称/行号/参数/局部变量） |
| `.workbuddy/analysis/carchive_toc.json` / `pyz_toc.json` | 打包清单 |
