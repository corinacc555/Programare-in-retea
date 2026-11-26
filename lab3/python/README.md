# Lab 3 – Bonus: the same chat in Python

A second implementation of the LAN chat, in Python, with the `socket` module only
(`socket`, `bind`, `setsockopt`, `sendto`, `recvfrom`). It speaks exactly the same protocol as
the C# chat in the parent folder, so Python peers and C# peers talk to each other in one chat.

## Install and run

Python 3.10 or newer. The only package is `cryptography`, for AES-GCM (the Python standard
library has PBKDF2 but no AES):

```
cd lab3/python
pip install -r requirements.txt
python main.py <name> <port> <passphrase>
```

Example: a C# peer and a Python peer in the same chat (same port, same passphrase):

```
cd lab3        && dotnet run -- Ana 5000 secret
cd lab3/python && python main.py Ion 5000 secret
```

The commands are the same: `text`, `/msg name text`, `/users`, `/quit`.
Each peer writes `chat-<name>.log` in the folder it is started from.

## The common format (what both chats agree on)

| What | Value |
|---|---|
| Datagram | one JSON object, UTF-8, at most 4096 bytes |
| Fields | `type`, `from`, `id`, and `text`, `nonce` when needed (absent fields are not sent) |
| Types | `join`, `here`, `taken`, `alive`, `bye`, `general`, `private` |
| General channel | multicast group `239.255.42.42`, on the port of the chat |
| Private messages | unicast to the sender's direct socket (the endpoint seen in `recvfrom`) |
| Key | PBKDF2-HMAC-SHA256, salt `pr-lab3-lan-chat`, 200 000 iterations, 32 bytes |
| Encryption | AES-256-GCM, random 12-byte nonce, 16-byte tag, the author's name as associated data |
| `text` of a private message | base64 of ciphertext + tag; `nonce` is base64 too |
| Timers | `alive` every 5 s, a peer silent for 15 s is gone, 1 s to collect the answers to `join` |

## Files

Each file has the same job as the C# file with the same name:

| File | C# file | What it does |
|---|---|---|
| `main.py` | `Program.cs` | reads and checks the arguments |
| `chat.py` | `Chat.cs` | the peer: keyboard, commands, every message type, heartbeat and timeout |
| `network.py` | `Network.cs` | creates the two sockets and sends datagrams |
| `message.py` | `Message.cs` | the message format: to JSON, from JSON, validation |
| `peers.py` | `Peers.cs` | who is online, under a lock |
| `encryption.py` | `Crypto.cs` | key derivation and AES-GCM |
| `log.py` | `Log.cs` | the log file |
