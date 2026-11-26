# Lab 3 – A LAN Chat

Peer-to-peer chat over UDP, written in C# (.NET 10) with the `Socket` class only
(`SocketType.Dgram`, no `UdpClient`). There is no server: every running copy is a peer.

## Build and run

```
dotnet build
dotnet run -- <name> <port> <passphrase>
```

Example, in three terminals (from the `lab3` folder):

```
dotnet run -- Ana 5000 secret
dotnet run -- Ion 5000 secret
dotnet run -- Maria 5000 secret
```

All peers of one chat use the same port and the same passphrase. They can run on the same
computer or on different computers in the same network. If the Wi-Fi blocks multicast, use
the hotspot of a phone.

| Command | What it does |
|---|---|
| `text` | sends text to everybody (general channel) |
| `/msg name text` | sends text to `name` only (private, encrypted) |
| `/users` | shows who is online and their status (active / inactive / not responding) |
| `/quit` | leaves the chat |

A message has at most 500 characters. Each peer writes `chat-<name>.log`: one line for every
datagram it sends, receives or drops (time, endpoint, kind, size).

## How it works

Each peer has two sockets:

| Socket | Bound to | Used for |
|---|---|---|
| group socket | `0.0.0.0:<port>`, `ReuseAddress`, joined to multicast `239.255.42.42` | receives the general channel |
| direct socket | `0.0.0.0:0` (a free port chosen by the system) | sends everything, receives private messages and replies |

Every datagram is one JSON message, for example:

```json
{"type":"general","from":"Ana","id":"3f2a9c1b","text":"hello everyone"}
```

| Type | Sent to | Meaning |
|---|---|---|
| `join` | group | I just started |
| `here` | the new peer | answer to `join`: I am online too |
| `taken` | the new peer | answer to `join`: your name is already used |
| `alive` | group | every 5 s; a peer silent for 15 s is considered gone |
| `bye` | group | I left with `/quit` |
| `general` | group | message for everybody |
| `private` | one peer | `text` = AES-GCM ciphertext + tag (base64), plus `nonce` |

- Every message also carries `idle`: how many seconds ago its author last typed a line.
- `/users` shows a status for each peer, and changes are announced in the chat:
  - `active`;
  - `inactive (1 min 20 s)`: the program runs, but nothing was typed for 60 s;
  - `not responding (9 s)`: no datagram for 7 s (an `alive` was lost); after 15 s the peer is removed.
- `id` is random for each process. A peer recognises its own messages by it (multicast comes
  back to the sender), and tells apart two processes that use the same name.
- The key is derived from the passphrase with PBKDF2-SHA256 (200 000 iterations, 32 bytes).
  Each private message gets a new random 12-byte nonce. The author's name is the associated
  data, so changing it breaks the tag.
- A datagram that is not valid (not JSON, unknown type, missing fields, over 4096 bytes,
  cannot be decrypted) is dropped and logged.

## Files

| File | What it does |
|---|---|
| `Program.cs` | reads and checks the arguments |
| `Chat.cs` | the peer: keyboard, commands, handling of every message type, heartbeat and timeout |
| `Network.cs` | creates the two sockets and sends datagrams |
| `Message.cs` | the message format: to JSON, from JSON, validation |
| `Peers.cs` | who is online (name, id, endpoint, last seen), under a lock |
| `Crypto.cs` | key derivation and AES-GCM encryption of private messages |
| `Log.cs` | the log file |

| Task | Where |
|---|---|
| 1. General channel | `Network.CreateGroupSocket`, `Chat.SendGeneral`, `Chat.OnMyEcho` |
| 2. Who is online | `Chat.Join`, `OnJoin`, `OnSameName`, `HeartbeatLoop`, `TimeoutLoop`, `Peers` |
| 3. Private messages | `Network.CreateDirectSocket`, `Chat.SendPrivate` |
| 4. Secure private messages | `Crypto`, `Chat.NewPrivate`, `Chat.ShowPrivate` |
