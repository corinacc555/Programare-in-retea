import re
import sys

from chat import Chat

# python main.py <name> <port> <passphrase>
# Acelasi chat ca cel in C#, scris in Python (bonus): aceleasi mesaje JSON, acelasi grup
# multicast, aceeasi criptare. Un peer Python si unul C# pot vorbi in acelasi chat.


def main():
    arguments = read_arguments(sys.argv[1:])
    if arguments is None:
        print("Usage: python main.py <name> <port> <passphrase>")
        print("  name: 1-20 letters, digits, _ or -   port: 1024-65535")
        return 1
    name, port, passphrase = arguments
    return Chat(name, port, passphrase).run()


# Intoarce (name, port, passphrase), sau None daca argumentele sunt gresite.
def read_arguments(args):
    if len(args) != 3:
        return None
    name, port_text, passphrase = args
    if not is_valid_name(name) or not port_text.isdigit() or passphrase == "":
        return None
    port = int(port_text)
    if port < 1024 or port > 65535:
        return None
    return name, port, passphrase


# numele ajunge si in numele fisierului de log, deci doar caractere sigure
def is_valid_name(name):
    return re.fullmatch(r"[A-Za-z0-9_-]{1,20}", name) is not None


if __name__ == "__main__":
    sys.exit(main())
