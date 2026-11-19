# Client / provider pentru coffee shop, scris in Python (bonus: al doilea limbaj).
# Vorbeste acelasi protocol text ca si clientul C#: o linie UTF-8 pentru fiecare mesaj.
#
#   python3 client.py <host> <port>             -> client
#   python3 client.py <host> <port> provider    -> provider

import os
import socket
import sys
import threading


def read_arguments():
    if len(sys.argv) not in (3, 4):
        return None
    host = sys.argv[1]
    try:
        port = int(sys.argv[2])
    except ValueError:
        return None
    if port < 1 or port > 65535:
        return None
    is_provider = False
    if len(sys.argv) == 4:
        if sys.argv[3].lower() != "provider":
            return None
        is_provider = True
    return host, port, is_provider


def send_line(sock, message):
    # sendall apeleaza send pana se trimit toti octetii
    sock.sendall((message + "\n").encode("utf-8"))


def receive_loop(sock):
    # Afiseaza fiecare mesaj de la server imediat ce soseste.
    # TCP e un flux: pastram octetii pana vine '\n', apoi afisam linia.
    pending = b""
    try:
        while True:
            data = sock.recv(1024)
            if not data:          # recv a intors 0 octeti: serverul a inchis conexiunea
                break
            pending += data
            while b"\n" in pending:
                line, pending = pending.split(b"\n", 1)
                print(line.decode("utf-8", errors="replace").rstrip("\r"), flush=True)
        print("Disconnected from the server.", flush=True)
    except OSError as e:
        print("Connection lost:", e, flush=True)
    os._exit(0)                   # inchidem programul, chiar daca input() asteapta


def keyboard_loop(sock):
    try:
        while True:
            try:
                text = input()
            except EOFError:
                break
            send_line(sock, text)
        sock.shutdown(socket.SHUT_WR)   # ii spunem serverului ca am terminat
    except OSError:
        print("Connection lost.")
        os._exit(0)


def main():
    arguments = read_arguments()
    if arguments is None:
        print("Usage: python3 client.py <host> <port> [provider]")
        return
    host, port, is_provider = arguments

    try:
        sock = socket.create_connection((host, port))
    except OSError as e:
        print(f"Cannot connect to {host}:{port}: {e}")
        return

    # Un thread primeste mesajele, thread-ul principal citeste tastatura.
    receiver = threading.Thread(target=receive_loop, args=(sock,), daemon=True)
    receiver.start()

    if is_provider:
        send_line(sock, "PROVIDER")
        print("You are the provider. Example: DELIVER coffee 20 croissant 10")
    else:
        print("Example: MENU, ORDER croissant 2 tea 1, QUIT")

    keyboard_loop(sock)
    receiver.join()


if __name__ == "__main__":
    main()
