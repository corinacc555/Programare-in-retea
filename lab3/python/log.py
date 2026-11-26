import datetime
import threading


# Fisierul chat-<name>.log: o linie pentru fiecare datagrama trimisa, primita sau aruncata.
class Log:
    def __init__(self, name):
        self._path = "chat-" + name + ".log"
        self._lock = threading.Lock()   # scriu din mai multe thread-uri

    def sent(self, to, kind, size):
        self._write("sent", to, kind, size)

    def received(self, sender, kind, size):
        self._write("received", sender, kind, size)

    def dropped(self, sender, reason, size):
        self._write("dropped", sender, reason, size)

    def _write(self, action, endpoint, kind, size):
        time = datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]
        address = f"{endpoint[0]}:{endpoint[1]}"
        line = f"{time} {action:<8} {address:<21} {kind:<20} {size} bytes\n"
        try:
            with self._lock:
                with open(self._path, "a", encoding="utf-8") as file:
                    file.write(line)
        except OSError:
            pass   # un log care nu se poate scrie nu trebuie sa opreasca chat-ul
