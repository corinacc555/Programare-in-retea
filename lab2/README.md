# Lab 2 – A Coffee Shop Server

TCP server for a coffee shop, written in C# (.NET 10) with the `Socket` class only
(no `TcpListener`, `TcpClient` or `NetworkStream`).

## Build

```
dotnet build Server
dotnet build Client
```

## Run

```
dotnet run --project Server -- <port>
dotnet run --project Client -- <host> <port>              # a client
dotnet run --project Client -- <host> <port> provider     # the provider
```

Example, in three terminals (from the `lab2` folder):

```
dotnet run --project Server -- 5000
dotnet run --project Client -- localhost 5000
dotnet run --project Client -- localhost 5000 provider
```

From another computer in the same network, use the IP of the server instead of `localhost`.

The server saves its stock in `stock.txt` (in the folder it is started from) and reads it
again when it restarts. Delete the file to start with a new random stock (10–50 of each product).

## Protocol

Text, one message per line (UTF-8, ended by `\n`, at most 256 bytes).

| Sends | Server answers |
|---|---|
| (connect) | `WELCOME ...` |
| `MENU` | `MENU coffee=17 tea=30 croissant=13` |
| `ORDER croissant 2 tea 1` | `ACCEPTED <id> <seconds>`, later `READY <id>` |
| | or `REFUSED not enough <product>, only <n> left` |
| `PROVIDER` | `OK you are the provider` (only one provider at a time) |
| `DELIVER coffee 20 croissant 10` | `DELIVERED coffee=.. tea=.. croissant=..` (provider only) |
| `QUIT` | `BYE` |
| anything wrong | `ERROR <reason>` |

The server also sends, on its own, `ALERT <product> <n> left` to the provider when an
order leaves a product below 5 (or when the provider connects, for every product already below 5).

Quantities are whole numbers from 1 to 1000. Preparation times: coffee 4 s, tea 3 s, croissant 2 s.
The server has one worker per logical processor; orders wait in a FIFO queue.

When a connection ends (QUIT, closed, killed, network gone), all its orders that are not
ready yet are canceled — also the one a worker is preparing — and their products go back to the stock.

## Files

| File | What it does |
|---|---|
| `Common/Protocol.cs` | `SendLine` and `LineReader`: cut the TCP byte stream into lines |
| `Server/Program.cs` | reads the port, starts the workers, accepts connections |
| `Server/ClientConnection.cs` | one thread per connection: reads and answers commands |
| `Server/Shop.cs` | the stock (under a lock), parsing of the items, saving to `stock.txt` |
| `Server/Kitchen.cs` | the orders, the FIFO queue and the workers |
| `Server/Provider.cs` | which connection is the provider, and the alerts |
| `Server/Log.cs` | log lines with time and endpoint |
| `Client/Program.cs` | the client / provider: one thread receives, the main thread reads the keyboard |
