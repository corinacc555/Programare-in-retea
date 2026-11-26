import threading
import time


class Peer:
    def __init__(self, name, id, endpoint):
        self.name = name
        self.id = id
        self.endpoint = endpoint          # (ip, port) al socket-ului lui direct, unic
        self.last_seen = time.monotonic()


# Cine e online. Il folosesc thread-urile de receive, de timeout si tastatura, deci are lock.
class Peers:
    def __init__(self):
        self._by_name = {}
        self._lock = threading.Lock()

    # Adauga sau reimprospateaza. Intoarce True doar daca e nou.
    def touch(self, name, id, endpoint):
        with self._lock:
            peer = self._by_name.get(name)
            if peer is not None:
                # acelasi nume, dar alt proces: nu il bag, ramane cel care era primul
                if peer.id == id:
                    peer.last_seen = time.monotonic()
                return False
            self._by_name[name] = Peer(name, id, endpoint)
            return True

    # Sterge doar daca e chiar procesul pe care il stiu (acelasi id).
    def remove(self, name, id):
        with self._lock:
            peer = self._by_name.get(name)
            if peer is None or peer.id != id:
                return False
            del self._by_name[name]
            return True

    def find_endpoint(self, name):
        with self._lock:
            peer = self._by_name.get(name)
            return peer.endpoint if peer is not None else None

    def all(self):
        with self._lock:
            return sorted(self._by_name.values(), key=lambda p: p.name)

    # Cei de la care n-a venit nimic de prea mult timp: ii scot si intorc numele lor.
    def remove_silent_for(self, seconds):
        with self._lock:
            now = time.monotonic()
            gone = [p.name for p in self._by_name.values() if now - p.last_seen > seconds]
            for name in gone:
                del self._by_name[name]
            return gone
