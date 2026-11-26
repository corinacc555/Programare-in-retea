import threading
import time
import uuid

import message as msg
from encryption import Crypto
from log import Log
from network import Network
from peers import Peers

JOIN_WAIT = 1          # secunde in care strang raspunsurile la join
HEARTBEAT_EVERY = 5    # la cate secunde spun "inca sunt aici"
GONE_AFTER = 15        # dupa cate secunde de liniste un peer e considerat plecat


# Un peer, la fel ca Chat.cs. Thread-ul principal citeste tastatura, restul pe thread-uri separate:
#   2 x receive_loop (cate unul pe socket), heartbeat_loop (alive la 5 s), timeout_loop (cine a disparut)
class Chat:
    def __init__(self, name, port, passphrase):
        self.my_name = name
        # id random per proces: cu el imi recunosc ecoul si deosebesc 2 procese cu acelasi nume
        self.my_id = uuid.uuid4().hex[:8]
        self.log = Log(name)
        self.net = Network(port, self.log)
        self.crypto = Crypto(passphrase)
        self.peers = Peers()
        self.running = True
        self.name_taken = False

    def run(self):
        # receive porneste inainte de join, ca sa prind raspunsurile la join
        start_thread(lambda: self.receive_loop(self.net.group_socket))
        start_thread(lambda: self.receive_loop(self.net.direct_socket))
        if not self.join():
            return 1

        start_thread(self.heartbeat_loop)
        start_thread(self.timeout_loop)
        self.read_keyboard()
        self.leave()
        return 0

    def new_message(self, type, text=None):
        return msg.Message(type, self.my_name, self.my_id, text)

    # ---------- intrare / iesire ----------

    # anunt pe toti ca am intrat, apoi 1 s strang raspunsurile "here" (sau "taken")
    def join(self):
        self.net.send_to_all(self.new_message(msg.JOIN))
        time.sleep(JOIN_WAIT)

        if self.name_taken:
            print(f"* the name {self.my_name} is already used, choose another one")
            self.net.send_to_all(self.new_message(msg.BYE))
            return False
        print(f"* you joined the chat as {self.my_name} (ip {self.net.local_address}, python)")
        self.show_users()
        return True

    def leave(self):
        self.running = False
        self.net.send_to_all(self.new_message(msg.BYE))
        print("* you left the chat")

    # ---------- tastatura ----------

    def read_keyboard(self):
        print("* commands: text | /msg name text | /users | /quit")
        while True:
            try:
                line = input()
            except (EOFError, KeyboardInterrupt):
                return   # ctrl+z / ctrl+c / stdin inchis
            line = line.strip()
            if line == "/quit":
                return
            self.handle_line(line)

    def handle_line(self, line):
        if line == "":
            return
        if line == "/users":
            self.show_users()
        elif line.startswith("/msg "):
            self.send_private(line[5:])
        elif line.startswith("/"):
            print("* unknown command")
        else:
            self.send_general(line)

    def send_general(self, text):
        if not fits_length(text):
            return
        # nu il afisez aici: il afisez cand se intoarce din retea (ecoul), asa apare o singura data
        self.net.send_to_all(self.new_message(msg.GENERAL, text))

    def send_private(self, name_and_text):
        parts = name_and_text.split(None, 1)
        if len(parts) < 2:
            print("* usage: /msg name text")
            return
        to, text = parts
        if not fits_length(text):
            return

        endpoint = self.peers.find_endpoint(to)
        if endpoint is None:
            print(f"* {to} is not online")
            return
        # un singur unicast direct la el, nu la toti cu numele lui inauntru
        self.net.send_to(self.new_private(text), endpoint)
        print(f"[private to {to}] {self.my_name}: {text}")

    def new_private(self, text):
        nonce, data = self.crypto.encrypt(text, self.my_name)
        message = self.new_message(msg.PRIVATE, data)
        message.nonce = nonce
        return message

    def show_users(self):
        others = self.peers.all()
        print(f"* online: {len(others) + 1}")
        print(f"*   {self.my_name} (you)")
        for peer in others:
            print(f"*   {peer.name} {peer.endpoint[0]}:{peer.endpoint[1]}")

    # ---------- primire ----------

    def receive_loop(self, sock):
        while self.running:
            try:
                # 65535 = cea mai mare datagrama UDP, ca sa nu se taie nimic
                data, sender = sock.recvfrom(65535)
            except OSError:
                # ex. pe Windows: am trimis un privat cuiva care a plecat -> ICMP "port unreachable"
                # apare aici ca eroare. nu e grav, continui
                continue
            try:
                self.on_datagram(data, sender)
            except Exception as e:
                # orice datagrama ciudata nu are voie sa opreasca thread-ul de primire
                self.log.dropped(sender, "error: " + type(e).__name__, len(data))

    def on_datagram(self, data, sender):
        message, error = msg.parse(data)
        if message is None:
            self.log.dropped(sender, error, len(data))
            return
        self.log.received(sender, message.type, len(data))
        self.handle(message, sender)

    def handle(self, message, sender):
        if message.id == self.my_id:
            self.on_my_echo(message)
        elif message.sender == self.my_name:
            self.on_same_name(message, sender)
        else:
            self.on_other_peer(message, sender)

    # multicastul se intoarce si la mine (loopback). din ecourile mele afisez doar textul
    def on_my_echo(self, message):
        if message.type == msg.GENERAL:
            show_general(message)

    # alt proces cu numele meu. daca pornesc 2 deodata, fiecare primeste join-ul celuilalt -> ies amandoi
    def on_same_name(self, message, sender):
        if message.type == msg.JOIN:
            self.net.send_to(self.new_message(msg.TAKEN), sender)   # el nu intra
        if message.type == msg.TAKEN:
            self.name_taken = True                                  # eu nu intru

    def on_other_peer(self, message, sender):
        if message.type == msg.JOIN:
            self.on_join(message, sender)
        elif message.type == msg.HERE:
            self.peers.touch(message.sender, message.id, sender)
        elif message.type == msg.ALIVE:
            self.on_alive(message, sender)
        elif message.type == msg.BYE:
            self.on_bye(message)
        elif message.type == msg.GENERAL:
            show_general(message)
        elif message.type == msg.PRIVATE:
            self.show_private(message, sender)

    # cineva nou: il adaug si ii raspund direct, ca sa afle de mine acum, nu peste 5 s
    def on_join(self, message, sender):
        if self.peers.touch(message.sender, message.id, sender):
            print(f"* {message.sender} joined the chat")
        self.net.send_to(self.new_message(msg.HERE), sender)

    # alive de la un necunoscut = i s-a pierdut join-ul, sau revine dupa timeout
    def on_alive(self, message, sender):
        if self.peers.touch(message.sender, message.id, sender):
            print(f"* {message.sender} joined the chat")

    def on_bye(self, message):
        if self.peers.remove(message.sender, message.id):
            print(f"* {message.sender} left the chat")

    def show_private(self, message, sender):
        text = self.crypto.decrypt(message.nonce, message.text, message.sender)
        if text is None or len(text) > msg.MAX_TEXT_LENGTH:
            self.log.dropped(sender, "cannot decrypt", message.size)
            return
        print(f"[private] {message.sender}: {text}")

    # ---------- timere ----------

    def heartbeat_loop(self):
        while self.running:
            time.sleep(HEARTBEAT_EVERY)
            if self.running:
                self.net.send_to_all(self.new_message(msg.ALIVE))

    # UDP n-are conexiune, deci nu aflu cand moare un proces. il consider plecat dupa 15 s de liniste
    # (= 3 alive-uri pierdute la rand, ca o singura datagrama pierduta sa nu-l scoata)
    def timeout_loop(self):
        while self.running:
            time.sleep(1)
            for name in self.peers.remove_silent_for(GONE_AFTER):
                print(f"* {name} left the chat (no news for {GONE_AFTER} s)")


def start_thread(work):
    # daemon = nu tine procesul in viata dupa /quit
    threading.Thread(target=work, daemon=True).start()


def fits_length(text):
    if len(text) <= msg.MAX_TEXT_LENGTH:
        return True
    print(f"* too long: {len(text)} characters, max {msg.MAX_TEXT_LENGTH}")
    return False


def show_general(message):
    print(f"[general] {message.sender}: {message.text}")
