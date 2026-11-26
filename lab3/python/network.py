import socket
import struct
import sys

# Acelasi grup multicast ca in Network.cs. Daca difera, cele doua chat-uri nu se aud.
GROUP = "239.255.42.42"


# Cele 2 socket-uri ale unui peer, la fel ca in C#:
#   group_socket:  0.0.0.0:<port chat>, comun pentru toti peers -> primeste canalul general (multicast)
#   direct_socket: 0.0.0.0:<port random>, doar al meu         -> primeste private + raspunsuri, si trimite TOT
class Network:
    def __init__(self, port, log):
        self._log = log
        self._group_endpoint = (GROUP, port)
        self.local_address = find_local_address()
        self.group_socket = create_group_socket(port)
        self.direct_socket = create_direct_socket(self.local_address)

    def send_to_all(self, message):
        self.send_to(message, self._group_endpoint)

    # Trimit mereu de pe direct_socket: asa cine primeste vede in recvfrom endpoint-ul meu unic.
    def send_to(self, message, endpoint):
        data = message.to_bytes()
        try:
            self.direct_socket.sendto(data, endpoint)
            self._log.sent(endpoint, message.type, len(data))
        except OSError as e:
            print("* could not send:", e)


# IP-ul placii de retea pe care iese ruta implicita (spre router), nu al unei placi virtuale.
def find_local_address():
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    try:
        # la UDP, connect nu trimite nimic pe retea, doar alege ruta si IP-ul local
        probe.connect(("8.8.8.8", 53))
        return probe.getsockname()[0]
    except OSError:
        return "0.0.0.0"   # fara ruta implicita: las sistemul sa aleaga
    finally:
        probe.close()


def create_group_socket(port):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    # fara asta, al 2-lea peer pe acelasi calculator (C# sau Python) ia "address already in use".
    # trebuie INAINTE de bind
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    if sys.platform == "darwin":
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEPORT, 1)   # pe macOS e nevoie si de asta
    # 0.0.0.0 = primesc pe toate placile de retea, nu doar pe un IP
    sock.bind(("0.0.0.0", port))

    # intru in grupul multicast pe FIECARE placa de retea, altfel sistemul nu-mi da
    # datagramele trimise la 239.255.42.42
    joined = False
    for address in local_ipv4_addresses():
        try:
            sock.setsockopt(socket.IPPROTO_IP, socket.IP_ADD_MEMBERSHIP, membership(GROUP, address))
            joined = True
        except OSError:
            pass   # placa care nu suporta multicast: o sar
    if not joined:
        sock.setsockopt(socket.IPPROTO_IP, socket.IP_ADD_MEMBERSHIP, membership(GROUP, "0.0.0.0"))
    return sock


def create_direct_socket(local_address):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    # port 0 = sistemul alege unul liber. e unic, deci un privat trimis aici ajunge doar la mine
    sock.bind(("0.0.0.0", 0))
    sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_TTL, 1)   # nu iese din reteaua locala
    if local_address != "0.0.0.0":
        # multicastul trimis iese pe placa reala, nu pe una virtuala
        sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_IF, socket.inet_aton(local_address))
    return sock


# structura ip_mreq: adresa grupului + adresa placii de retea, cate 4 bytes
def membership(group, address):
    return struct.pack("4s4s", socket.inet_aton(group), socket.inet_aton(address))


# IP-urile IPv4 ale calculatorului, fara loopback.
def local_ipv4_addresses():
    addresses = {find_local_address()}
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            addresses.add(info[4][0])
    except OSError:
        pass
    return [a for a in addresses if a != "0.0.0.0" and not a.startswith("127.")]
