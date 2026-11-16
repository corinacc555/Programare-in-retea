# Lab 2 – A Coffee Shop Server

TCP server for a coffee shop, written in C# (.NET 10) with the `Socket` class only.

## Build

```
dotnet build Server
dotnet build Client
```

## Run

```
dotnet run --project Server -- <port>
dotnet run --project Client -- <host> <port>            (client)
dotnet run --project Client -- <host> <port> provider   (provider)
```

Example: `dotnet run --project Server -- 5000`, `dotnet run --project Client -- localhost 5000`
and `dotnet run --project Client -- localhost 5000 provider`.

## Protocol

Text, one message per line (UTF-8, ended by `\n`, at most 256 bytes).
Quantities are whole numbers from 1 to 1000.

| Sends | Server answers |
|---|---|
| `MENU` | `MENU coffee=17 tea=30 croissant=13` |
| `ORDER croissant 2 tea 1` | `ACCEPTED <id> <seconds>`, later `READY <id>` |
| | or `REFUSED not enough <product>, only <n> left` |
| `PROVIDER` | `OK you are the provider` (only one provider at a time) |
| `DELIVER coffee 20 croissant 10` | `DELIVERED <stock>` (provider only) |
| `QUIT` | `BYE` |
| anything wrong | `ERROR <reason>` |

When a product falls below 5, the server sends the provider `ALERT <product> only <n> left`.
A provider that connects later gets an alert for every product that is still below 5.

When a client disconnects, its orders are cancelled and their products go back to the stock.

Preparation times: coffee 4 s, tea 3 s, croissant 2 s.
