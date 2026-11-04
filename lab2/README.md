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
dotnet run --project Client -- <host> <port>
```

Example: `dotnet run --project Server -- 5000` and `dotnet run --project Client -- localhost 5000`.

## Protocol

Text, one message per line (UTF-8, ended by `\n`, at most 256 bytes).

| Client sends | Server answers |
|---|---|
| `MENU` | `MENU coffee=17 tea=30 croissant=13` |
| `ORDER croissant 2 tea 1` | `ACCEPTED <id> <seconds>`, later `READY <id>` |
| | or `REFUSED not enough <product>, only <n> left` |
| `QUIT` | `BYE` |
| anything wrong | `ERROR <reason>` |

Preparation times: coffee 4 s, tea 3 s, croissant 2 s.
